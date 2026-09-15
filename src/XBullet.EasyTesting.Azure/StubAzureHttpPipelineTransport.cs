using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Azure;

/// <summary>A scripted Azure SDK transport that records requests without using the network.</summary>
public sealed class StubAzureHttpPipelineTransport : HttpPipelineTransport, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly Queue<Func<RecordedAzureRequest, Response>> _responses = [];
    private readonly List<RecordedAzureRequest> _requests = [];

    /// <summary>Gets a stable copy of recorded requests.</summary>
    public IReadOnlyList<RecordedAzureRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Gets the number of recorded requests.</summary>
    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    /// <summary>Gets the number of arranged responses not yet consumed.</summary>
    public int RemainingResponseCount
    {
        get
        {
            lock (_gate)
            {
                return _responses.Count;
            }
        }
    }

    /// <summary>Enqueues a response status and optional body for one request.</summary>
    public StubAzureHttpPipelineTransport Respond(
        int status = 200,
        BinaryData? content = null,
        string? contentType = null)
    {
        return Respond(_ =>
        {
            var response = new TestAzureResponse(status);
            if (content is not null)
            {
                response.WithContent(content, contentType);
            }

            return response;
        });
    }

    /// <summary>Enqueues a JSON response for one request.</summary>
    public StubAzureHttpPipelineTransport RespondJson<T>(
        T value,
        int status = 200,
        JsonSerializerOptions? serializerOptions = null) =>
        Respond(_ => new TestAzureResponse(status).WithJsonContent(value, serializerOptions));

    /// <summary>Enqueues a request-aware response factory for one request.</summary>
    public StubAzureHttpPipelineTransport Respond(
        Func<RecordedAzureRequest, Response> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        lock (_gate)
        {
            _responses.Enqueue(responseFactory);
        }

        return this;
    }

    /// <summary>Verifies a method and absolute URI, relative path, or relative path-and-query call count.</summary>
    public StubAzureHttpPipelineTransport VerifyCalled(
        RequestMethod method,
        string requestUri,
        int expectedCount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        return Verify(
            request => request.Method == method && MatchesUri(request.Uri, requestUri),
            expectedCount,
            $"{method} {requestUri}");
    }

    /// <summary>Verifies the number of requests accepted by a custom predicate.</summary>
    public StubAzureHttpPipelineTransport Verify(
        Func<RecordedAzureRequest, bool> predicate,
        int expectedCount,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        var requests = Requests;
        var actualCount = requests.Count(predicate);
        if (actualCount != expectedCount)
        {
            var expectation = string.IsNullOrWhiteSpace(description)
                ? "the request predicate"
                : description;
            throw new AzureTransportVerificationException(
                $"Expected {expectation} to match {expectedCount} request(s), " +
                $"but it matched {actualCount}.{Environment.NewLine}" +
                FormatRequests(requests));
        }

        return this;
    }

    /// <summary>Clears arranged responses and recorded requests.</summary>
    public StubAzureHttpPipelineTransport Reset()
    {
        lock (_gate)
        {
            _responses.Clear();
            _requests.Clear();
        }

        return this;
    }

    /// <inheritdoc />
    public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();

    /// <inheritdoc />
    public override void Process(HttpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = CaptureRequest(message.Request, message.CancellationToken);
        message.Response = CreateResponse(request);
    }

    /// <inheritdoc />
    public override async ValueTask ProcessAsync(HttpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = await CaptureRequestAsync(message.Request, message.CancellationToken);
        message.Response = CreateResponse(request);
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requests = Requests.Select(request => new
        {
            Method = request.Method.ToString(),
            Uri = request.Uri.GetLeftPart(UriPartial.Path),
            HeaderNames = request.Headers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            ContentLength = request.Content?.ToMemory().Length ?? 0
        }).ToArray();
        return ValueTask.FromResult<object?>(new
        {
            CallCount,
            RemainingResponseCount,
            Requests = requests
        });
    }

    private Response CreateResponse(RecordedAzureRequest request)
    {
        Func<RecordedAzureRequest, Response>? responseFactory;
        lock (_gate)
        {
            _requests.Add(request);
            responseFactory = _responses.Count == 0 ? null : _responses.Dequeue();
        }

        return responseFactory?.Invoke(request)
            ?? new TestAzureResponse(501, "No Azure test response was arranged.")
                .WithContent(BinaryData.FromString(
                    $"No response was arranged for {request.Method} {request.Uri}."), "text/plain");
    }

    private static RecordedAzureRequest CaptureRequest(
        Request request,
        CancellationToken cancellationToken)
    {
        BinaryData? content = null;
        if (request.Content is not null)
        {
            using var stream = new MemoryStream();
            request.Content.WriteTo(stream, cancellationToken);
            content = BinaryData.FromBytes(stream.ToArray());
        }

        return CreateRecordedRequest(request, content);
    }

    private static async ValueTask<RecordedAzureRequest> CaptureRequestAsync(
        Request request,
        CancellationToken cancellationToken)
    {
        BinaryData? content = null;
        if (request.Content is not null)
        {
            using var stream = new MemoryStream();
            await request.Content.WriteToAsync(stream, cancellationToken);
            content = BinaryData.FromBytes(stream.ToArray());
        }

        return CreateRecordedRequest(request, content);
    }

    private static RecordedAzureRequest CreateRecordedRequest(Request request, BinaryData? content)
    {
        var headers = request.Headers.ToDictionary(
            header => header.Name,
            header => header.Value,
            StringComparer.OrdinalIgnoreCase);
        return new RecordedAzureRequest(
            request.Method,
            request.Uri.ToUri(),
            headers,
            content);
    }

    private static bool MatchesUri(Uri actual, string expected)
    {
        // A root-relative HTTP path is parsed as an absolute file URI on Unix,
        // so classify it as a path before attempting absolute URI parsing.
        if (!expected.StartsWith("/", StringComparison.Ordinal) &&
            Uri.TryCreate(expected, UriKind.Absolute, out var absolute))
        {
            return actual == absolute;
        }

        var actualValue = expected.Contains('?', StringComparison.Ordinal)
            ? actual.PathAndQuery
            : actual.AbsolutePath;
        return string.Equals(actualValue, expected, StringComparison.Ordinal);
    }

    private static string FormatRequests(IReadOnlyList<RecordedAzureRequest> requests) =>
        requests.Count == 0
            ? "Recorded requests: none."
            : "Recorded requests:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                requests.Select(request => $"- {request.Method} {request.Uri}"));
}
