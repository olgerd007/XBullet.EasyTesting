using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Http;

/// <summary>Builds the ordered responses returned by a single HTTP stub rule.</summary>
public sealed class StubHttpResponseSequenceBuilder
{
    private readonly List<Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>> _responses = [];
    private TimeSpan _nextDelay;

    /// <summary>Delays the next configured sequence response.</summary>
    public StubHttpResponseSequenceBuilder WithDelay(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        _nextDelay = delay;
        return this;
    }

    /// <summary>Adds a response containing no body.</summary>
    public StubHttpResponseSequenceBuilder Respond(HttpStatusCode statusCode) =>
        Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

    /// <summary>Adds a JSON response.</summary>
    public StubHttpResponseSequenceBuilder RespondJson<T>(
        T value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        JsonSerializerOptions? serializerOptions = null) =>
        Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(value, options: serializerOptions)
        }));

    /// <summary>Adds a UTF-8 text response.</summary>
    public StubHttpResponseSequenceBuilder RespondText(
        string value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(value, Encoding.UTF8, mediaType)
        }));
    }

    /// <summary>Adds a response created from the captured request.</summary>
    public StubHttpResponseSequenceBuilder Respond(
        Func<StubHttpRequest, HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return Add((request, _) => Task.FromResult(responseFactory(request)));
    }

    /// <summary>Adds an asynchronous response created from the captured request.</summary>
    public StubHttpResponseSequenceBuilder RespondAsync(
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return Add(responseFactory);
    }

    /// <summary>Adds an exception response.</summary>
    public StubHttpResponseSequenceBuilder Throw(Func<StubHttpRequest, Exception> exceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(exceptionFactory);
        return Add((request, _) =>
            Task.FromException<HttpResponseMessage>(exceptionFactory(request)));
    }

    /// <summary>Adds an immediately cancelled response.</summary>
    public StubHttpResponseSequenceBuilder Cancel() =>
        Add((_, _) => Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true)));

    /// <summary>Adds a response that is cancelled after the supplied delay.</summary>
    public StubHttpResponseSequenceBuilder CancelAfter(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        return Add(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            throw new OperationCanceledException(
                "The arranged outbound HTTP response was cancelled.",
                new CancellationToken(true));
        });
    }

    /// <summary>Adds intentionally invalid JSON with a JSON content type.</summary>
    public StubHttpResponseSequenceBuilder RespondMalformedJson(
        string content = "{\"incomplete\":",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        EnsureMalformedJson(content);
        return Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }));
    }

    /// <summary>Adds content that throws an I/O exception while it is being consumed.</summary>
    public StubHttpResponseSequenceBuilder RespondTruncated(
        string partialContent,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(partialContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new TruncatedHttpContent(partialContent, mediaType)
        }));
    }

    /// <summary>Adds a response that waits until the request is cancelled or its client times out.</summary>
    public StubHttpResponseSequenceBuilder Timeout() =>
        Add(WaitForCancellationAsync);

    /// <summary>Adds a response that throws <see cref="TimeoutException"/> after a delay.</summary>
    public StubHttpResponseSequenceBuilder TimeoutAfter(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        return Add(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            throw new TimeoutException($"The arranged outbound HTTP call timed out after {delay}.");
        });
    }

    internal IReadOnlyList<Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>> Build()
    {
        if (_nextDelay != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "WithDelay must be followed by a response in the HTTP response sequence.");
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("An HTTP response sequence requires at least one response.");
        }

        return _responses.ToArray();
    }

    private StubHttpResponseSequenceBuilder Add(
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        var delay = _nextDelay;
        _nextDelay = TimeSpan.Zero;
        _responses.Add(delay == TimeSpan.Zero
            ? responseFactory
            : async (request, cancellationToken) =>
            {
                await Task.Delay(delay, cancellationToken);
                return await responseFactory(request, cancellationToken);
            });
        return this;
    }

    private static async Task<HttpResponseMessage> WaitForCancellationAsync(
        StubHttpRequest _,
        CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        throw new TimeoutException("The arranged outbound HTTP call did not time out or get cancelled.");
    }

    private static void ValidateDelay(TimeSpan delay, string parameterName)
    {
        if (delay < TimeSpan.Zero || delay == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Delay must be zero or greater.");
        }
    }

    private static void EnsureMalformedJson(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return;
        }

        throw new ArgumentException(
            "The malformed JSON response content must not be valid JSON.",
            nameof(content));
    }
}
