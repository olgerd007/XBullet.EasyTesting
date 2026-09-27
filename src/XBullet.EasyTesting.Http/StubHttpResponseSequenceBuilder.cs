using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Http;

/// <summary>Builds the ordered responses returned by a single HTTP stub rule.</summary>
/// <remarks>
/// This mutable builder is not thread-safe and is intended for use only inside the synchronous
/// sequence-configuration callback. Each configured response is consumed by one matching request;
/// concurrent requests receive distinct sequence positions.
/// </remarks>
public sealed class StubHttpResponseSequenceBuilder
{
    private readonly List<Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>> _responses = [];
    private TimeSpan _nextDelay;

    /// <summary>Delays the next configured sequence response.</summary>
    /// <param name="delay">
    /// The non-negative delay applied only to the next response added to the sequence. Zero clears
    /// any pending delay. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is not accepted,
    /// and request cancellation can end the delay early.
    /// </param>
    /// <returns>This sequence builder so the delayed response can be added.</returns>
    public StubHttpResponseSequenceBuilder WithDelay(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        _nextDelay = delay;
        return this;
    }

    /// <summary>Adds a response containing no body.</summary>
    /// <param name="statusCode">The HTTP status code returned at this sequence position.</param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder Respond(HttpStatusCode statusCode) =>
        Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

    /// <summary>Adds a JSON response.</summary>
    /// <typeparam name="T">The type serialized as the JSON response body.</typeparam>
    /// <param name="value">
    /// The response value captured until this sequence position is consumed. The builder does not
    /// own or dispose it; callers must not mutate it concurrently with response serialization.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code for the response. The default is <see cref="HttpStatusCode.OK"/>.
    /// </param>
    /// <param name="serializerOptions">
    /// Options passed to the JSON content, or <see langword="null"/> to use the framework's web
    /// defaults. The builder retains but does not own or mutate non-null options.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder RespondJson<T>(
        T value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        JsonSerializerOptions? serializerOptions = null) =>
        Add((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(value, options: serializerOptions)
        }));

    /// <summary>Adds a UTF-8 text response.</summary>
    /// <param name="value">The non-null text returned at this sequence position.</param>
    /// <param name="statusCode">
    /// The HTTP status code for the response. The default is <see cref="HttpStatusCode.OK"/>.
    /// </param>
    /// <param name="mediaType">
    /// The non-empty response media type. The default is <c>text/plain</c>; UTF-8 is always used.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
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
    /// <param name="responseFactory">
    /// The non-null factory invoked once, when this sequence position is consumed, with the
    /// handler-owned captured request. Other sequence factories may run concurrently. The factory
    /// must return a non-null response whose ownership passes to the HTTP caller.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder Respond(
        Func<StubHttpRequest, HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return Add((request, _) => Task.FromResult(responseFactory(request)));
    }

    /// <summary>Adds an asynchronous response created from the captured request.</summary>
    /// <param name="responseFactory">
    /// The non-null factory invoked once, when this sequence position is consumed, with the
    /// handler-owned captured request and that send operation's cancellation token. Other sequence
    /// factories may run concurrently. The returned task must produce a non-null response whose
    /// ownership passes to the HTTP caller; cancellation and other failures are propagated.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder RespondAsync(
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return Add(responseFactory);
    }

    /// <summary>Adds an exception response.</summary>
    /// <param name="exceptionFactory">
    /// The non-null factory invoked once, when this sequence position is consumed, with the
    /// handler-owned captured request. Other sequence factories may run concurrently. It must
    /// return a non-null exception, which is recorded and propagated to the HTTP caller.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder Throw(Func<StubHttpRequest, Exception> exceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(exceptionFactory);
        return Add((request, _) =>
            Task.FromException<HttpResponseMessage>(exceptionFactory(request)));
    }

    /// <summary>Adds an immediately cancelled response.</summary>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder Cancel() =>
        Add((_, _) => Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true)));

    /// <summary>Adds a response that is cancelled after the supplied delay.</summary>
    /// <param name="delay">
    /// The non-negative delay before arranged cancellation. Zero cancels without waiting.
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is not accepted. Request cancellation
    /// can end the delay earlier.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
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
    /// <param name="content">
    /// The non-null invalid JSON text returned as UTF-8 <c>application/json</c>. The default is
    /// <c>{"incomplete":</c>. Valid JSON is rejected when the sequence is configured.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code for the response. The default is <see cref="HttpStatusCode.OK"/>.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
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
    /// <param name="partialContent">
    /// The non-null text made available before content consumption fails. An empty value is
    /// accepted.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code for the response. The default is <see cref="HttpStatusCode.OK"/>.
    /// </param>
    /// <param name="mediaType">
    /// The non-empty content media type. The default is <c>application/octet-stream</c>.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
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
    /// <returns>This sequence builder so additional responses can be appended.</returns>
    public StubHttpResponseSequenceBuilder Timeout() =>
        Add(WaitForCancellationAsync);

    /// <summary>Adds a response that throws <see cref="TimeoutException"/> after a delay.</summary>
    /// <param name="delay">
    /// The non-negative delay before the exception is thrown. Zero throws without waiting.
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is not accepted. Request cancellation
    /// can end the delay earlier instead.
    /// </param>
    /// <returns>This sequence builder so additional responses can be appended.</returns>
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
