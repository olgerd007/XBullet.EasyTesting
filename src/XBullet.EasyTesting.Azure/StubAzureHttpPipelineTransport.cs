using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using XBullet.EasyTesting.Diagnostics;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Azure;

/// <summary>A scripted Azure SDK transport that records requests without using the network.</summary>
public sealed class StubAzureHttpPipelineTransport : HttpPipelineTransport, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly Queue<Func<RecordedAzureRequest, Response>> _responses = [];
    private readonly List<RecordedAzureRequest> _requests = [];

    /// <summary>Gets a stable copy of recorded requests.</summary>
    /// <value>
    /// A newly allocated snapshot in call order. The caller may retain the list. Individual records
    /// contain unredacted URI queries, header values, and bodies that may be sensitive.
    /// </value>
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
    /// <value>The total number of synchronous and asynchronous requests recorded since construction or reset.</value>
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
    /// <value>The non-negative number of queued response arrangements.</value>
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
    /// <param name="status">
    /// The HTTP status code from 100 through 599. The default is 200. Validation occurs when the
    /// response is consumed.
    /// </param>
    /// <param name="content">
    /// The optional response body retained until the arrangement is consumed. When
    /// <see langword="null"/>, the response has no body.
    /// </param>
    /// <param name="contentType">
    /// The optional content-type header. A null, empty, or whitespace value is omitted, and the value
    /// is ignored when <paramref name="content"/> is <see langword="null"/>.
    /// </param>
    /// <returns>This transport, for chaining. One response is appended to the thread-safe queue.</returns>
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
    /// <typeparam name="T">The type of value to serialize.</typeparam>
    /// <param name="value">
    /// The value retained and serialized when the response is consumed; nullable values are accepted
    /// when <typeparamref name="T"/> permits them.
    /// </param>
    /// <param name="status">
    /// The HTTP status code from 100 through 599. The default is 200. Validation occurs when consumed.
    /// </param>
    /// <param name="serializerOptions">
    /// Optional JSON options retained until serialization, or <see langword="null"/> to use Azure
    /// <see cref="BinaryData"/> defaults.
    /// </param>
    /// <returns>This transport, for chaining. One JSON response is appended to the queue.</returns>
    public StubAzureHttpPipelineTransport RespondJson<T>(
        T value,
        int status = 200,
        JsonSerializerOptions? serializerOptions = null) =>
        Respond(_ => new TestAzureResponse(status).WithJsonContent(value, serializerOptions));

    /// <summary>Enqueues a request-aware response factory for one request.</summary>
    /// <param name="responseFactory">
    /// A non-null callback retained until the next queued response is consumed, then invoked once with
    /// that request's stable record. It must return a non-null response whose ownership transfers to
    /// the Azure pipeline.
    /// </param>
    /// <returns>This transport, for chaining. The factory is appended to the thread-safe queue.</returns>
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
    /// <param name="method">The exact Azure request method to match.</param>
    /// <param name="requestUri">
    /// A non-empty absolute URI, root-relative path, or root-relative path and query. Absolute URIs
    /// match in full; relative values match the path, including the query only when one is supplied.
    /// Matching is ordinal and case-sensitive.
    /// </param>
    /// <param name="expectedCount">The non-negative number of matching requests expected. The default is one.</param>
    /// <returns>This transport when the expectation succeeds; otherwise, an exception is thrown.</returns>
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
            $"{method} {UriDiagnosticFormatter.Format(requestUri)}");
    }

    /// <summary>Verifies the number of requests accepted by a custom predicate.</summary>
    /// <param name="predicate">
    /// A non-null callback invoked once for each request in a stable snapshot. It returns
    /// <see langword="true"/> for matching requests and must not mutate transport state.
    /// </param>
    /// <param name="expectedCount">The non-negative number of accepted requests expected.</param>
    /// <param name="description">
    /// Optional expectation text used in a failure message, or <see langword="null"/>, empty, or
    /// whitespace to use a generic description. Do not include secrets in this text.
    /// </param>
    /// <returns>This transport when the expectation succeeds; otherwise, an exception is thrown.</returns>
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
    /// <returns>This transport, for chaining. Configured object identity is preserved.</returns>
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
                    $"No response was arranged for {request.Method} " +
                    $"{UriDiagnosticFormatter.Format(request.Uri)}."), "text/plain");
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
                requests.Select(request =>
                    $"- {request.Method} {UriDiagnosticFormatter.Format(request.Uri)}"));
}
