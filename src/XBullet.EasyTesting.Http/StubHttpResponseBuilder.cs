using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Http;

/// <summary>Defines the response returned by one outbound HTTP request rule.</summary>
public sealed class StubHttpResponseBuilder
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly StubHttpMessageHandler _handler;
    private readonly HttpMethod _method;
    private readonly string _requestUri;
    private readonly List<StubRequestPredicate> _predicates = [];
    private bool _hasQueryParameterMatcher;
    private TimeSpan _delay;

    internal StubHttpResponseBuilder(
        StubHttpMessageHandler handler,
        HttpMethod method,
        string requestUri)
    {
        _handler = handler;
        _method = method;
        _requestUri = requestUri;
    }

    /// <summary>Requires the request to contain an exact header value.</summary>
    public StubHttpResponseBuilder WithRequestHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _predicates.Add(new StubRequestPredicate(
            $"header '{name}' containing value '{value}'",
            request =>
                request.Headers.TryGetValue(name, out var values) &&
                values.Contains(value, StringComparer.Ordinal)));
        return this;
    }

    /// <summary>Requires the request query string to contain an exact parameter value.</summary>
    public StubHttpResponseBuilder WithQueryParameter(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _hasQueryParameterMatcher = true;
        _predicates.Add(new StubRequestPredicate(
            $"query parameter '{name}' containing value '{value}'",
            request => GetQueryParameterValues(request.RequestUri, name)
                .Contains(value, StringComparer.Ordinal)));
        return this;
    }

    /// <summary>Requires the values of a request query parameter to satisfy a predicate.</summary>
    public StubHttpResponseBuilder WithQueryParameter(
        string name,
        Func<IReadOnlyList<string>, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);
        _hasQueryParameterMatcher = true;
        _predicates.Add(new StubRequestPredicate(
            $"predicate for query parameter '{name}'",
            request => predicate(GetQueryParameterValues(request.RequestUri, name))));
        return this;
    }

    /// <summary>Requires the request body to exactly match the supplied text.</summary>
    public StubHttpResponseBuilder WithRequestBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _predicates.Add(new StubRequestPredicate(
            "the configured exact request body",
            request => string.Equals(request.Body, body, StringComparison.Ordinal)));
        return this;
    }

    /// <summary>Requires the request JSON body to structurally match the supplied value.</summary>
    public StubHttpResponseBuilder WithJsonRequestBody<T>(
        T value,
        JsonSerializerOptions? serializerOptions = null)
    {
        var expected = JsonSerializer.SerializeToNode(
            value,
            serializerOptions ?? DefaultSerializerOptions);
        _predicates.Add(new StubRequestPredicate(
            "the configured structural JSON request body",
            request => JsonMatches(request.Body, expected)));
        return this;
    }

    /// <summary>Requires a root JSON property to structurally match the supplied value.</summary>
    public StubHttpResponseBuilder WithJsonProperty<T>(
        string propertyName,
        T expectedValue,
        JsonSerializerOptions? serializerOptions = null)
    {
        var path = JsonPathMatcher.RootProperty(propertyName);
        var expected = JsonSerializer.SerializeToNode(
            expectedValue,
            serializerOptions ?? DefaultSerializerOptions);
        _predicates.Add(new StubRequestPredicate(
            $"JSON property '{propertyName}' equal to the configured value",
            request => JsonPathMatcher.Matches(request.Body, path, expected)));
        return this;
    }

    /// <summary>Requires a root JSON property to satisfy a predicate.</summary>
    public StubHttpResponseBuilder WithJsonProperty(
        string propertyName,
        Func<JsonElement, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var path = JsonPathMatcher.RootProperty(propertyName);
        _predicates.Add(new StubRequestPredicate(
            $"predicate for JSON property '{propertyName}'",
            request => JsonPathMatcher.Matches(request.Body, path, predicate)));
        return this;
    }

    /// <summary>
    /// Requires a JSON path to structurally match the supplied value. Dot-separated properties and
    /// zero-based array indexes are supported, for example <c>$.items[0].sku</c>.
    /// </summary>
    public StubHttpResponseBuilder WithJsonPath<T>(
        string path,
        T expectedValue,
        JsonSerializerOptions? serializerOptions = null)
    {
        var parsedPath = JsonPathMatcher.Parse(path);
        var expected = JsonSerializer.SerializeToNode(
            expectedValue,
            serializerOptions ?? DefaultSerializerOptions);
        _predicates.Add(new StubRequestPredicate(
            $"JSON path '{path}' equal to the configured value",
            request => JsonPathMatcher.Matches(request.Body, parsedPath, expected)));
        return this;
    }

    /// <summary>
    /// Requires a JSON path to satisfy a predicate. Dot-separated properties and zero-based array
    /// indexes are supported, for example <c>$.items[0].quantity</c>.
    /// </summary>
    public StubHttpResponseBuilder WithJsonPath(
        string path,
        Func<JsonElement, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var parsedPath = JsonPathMatcher.Parse(path);
        _predicates.Add(new StubRequestPredicate(
            $"predicate for JSON path '{path}'",
            request => JsonPathMatcher.Matches(request.Body, parsedPath, predicate)));
        return this;
    }

    /// <summary>Adds a custom request predicate to this response rule.</summary>
    public StubHttpResponseBuilder WithRequest(
        Func<StubHttpRequest, bool> predicate,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicates.Add(new StubRequestPredicate(
            string.IsNullOrWhiteSpace(description)
                ? "the custom request predicate"
                : description,
            predicate));
        return this;
    }

    /// <summary>Delays the arranged response while observing request cancellation.</summary>
    public StubHttpResponseBuilder WithDelay(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        _delay = delay;
        return this;
    }

    /// <summary>Adds a response containing no body.</summary>
    public StubHttpMessageHandler Respond(HttpStatusCode statusCode)
    {
        return AddResponse(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
    }

    /// <summary>Adds a JSON response.</summary>
    public StubHttpMessageHandler RespondJson<T>(
        T value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        JsonSerializerOptions? serializerOptions = null)
    {
        return AddResponse(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(value, options: serializerOptions)
            }));
    }

    /// <summary>Adds a UTF-8 text response.</summary>
    public StubHttpMessageHandler RespondText(
        string value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return AddResponse(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(value, Encoding.UTF8, mediaType)
            }));
    }

    /// <summary>Adds a response created from the captured request.</summary>
    public StubHttpMessageHandler Respond(
        Func<StubHttpRequest, HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return AddResponse(
            (request, _) => Task.FromResult(responseFactory(request)));
    }

    /// <summary>Adds an asynchronous response created from the captured request.</summary>
    public StubHttpMessageHandler RespondAsync(
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        return AddResponse(responseFactory);
    }

    /// <summary>Throws an exception when this rule matches.</summary>
    public StubHttpMessageHandler Throw(Func<StubHttpRequest, Exception> exceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(exceptionFactory);
        return AddResponse(
            (request, _) => Task.FromException<HttpResponseMessage>(exceptionFactory(request)));
    }

    /// <summary>Cancels the arranged response immediately.</summary>
    public StubHttpMessageHandler Cancel() =>
        AddResponse((_, _) => Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true)));

    /// <summary>Cancels the arranged response after the supplied delay.</summary>
    public StubHttpMessageHandler CancelAfter(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        return AddResponse(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            throw new OperationCanceledException(
                "The arranged outbound HTTP response was cancelled.",
                new CancellationToken(true));
        });
    }

    /// <summary>Returns intentionally invalid JSON with a JSON content type.</summary>
    public StubHttpMessageHandler RespondMalformedJson(
        string content = "{\"incomplete\":",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        EnsureMalformedJson(content);
        return AddResponse((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }));
    }

    /// <summary>Returns content that throws an I/O exception while it is being consumed.</summary>
    public StubHttpMessageHandler RespondTruncated(
        string partialContent,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(partialContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return AddResponse((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new TruncatedHttpContent(partialContent, mediaType)
        }));
    }

    /// <summary>Waits until the request is cancelled or its <see cref="HttpClient"/> times out.</summary>
    public StubHttpMessageHandler Timeout() =>
        AddResponse(WaitForCancellationAsync);

    /// <summary>Throws <see cref="TimeoutException"/> after the supplied delay.</summary>
    public StubHttpMessageHandler TimeoutAfter(TimeSpan delay)
    {
        ValidateDelay(delay, nameof(delay));
        return AddResponse(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            throw new TimeoutException($"The arranged outbound HTTP call timed out after {delay}.");
        });
    }

    /// <summary>Adds an ordered set of responses for consecutive matching requests.</summary>
    public StubHttpMessageHandler RespondSequence(
        Action<StubHttpResponseSequenceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var sequenceBuilder = new StubHttpResponseSequenceBuilder();
        configure(sequenceBuilder);
        var responses = sequenceBuilder.Build();
        var nextResponse = -1;

        return AddResponse(async (request, cancellationToken) =>
        {
            var responseIndex = Interlocked.Increment(ref nextResponse);
            if (responseIndex >= responses.Count)
            {
                throw new StubHttpSequenceExhaustedException(
                    $"The response sequence for {_method} {_requestUri} contains " +
                    $"{responses.Count} response(s), but call {responseIndex + 1} was received.");
            }

            return await responses[responseIndex](request, cancellationToken);
        });
    }

    private StubHttpMessageHandler AddResponse(
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        var delay = _delay;
        var delayedResponseFactory = delay == TimeSpan.Zero
            ? responseFactory
            : async (StubHttpRequest request, CancellationToken cancellationToken) =>
            {
                await Task.Delay(delay, cancellationToken);
                return await responseFactory(request, cancellationToken);
            };

        return _handler.AddRule(
            _method,
            _requestUri,
            _hasQueryParameterMatcher,
            _predicates.ToArray(),
            delayedResponseFactory);
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

    private static IReadOnlyList<string> GetQueryParameterValues(Uri? uri, string name)
    {
        if (uri is null)
        {
            return [];
        }

        var uriText = uri.IsAbsoluteUri ? uri.Query : uri.OriginalString;
        var queryStart = uriText.IndexOf('?');
        if (queryStart >= 0)
        {
            uriText = uriText[(queryStart + 1)..];
        }
        else if (!uri.IsAbsoluteUri)
        {
            return [];
        }

        var fragmentStart = uriText.IndexOf('#');
        if (fragmentStart >= 0)
        {
            uriText = uriText[..fragmentStart];
        }

        var values = new List<string>();
        foreach (var component in uriText.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = component.IndexOf('=');
            var encodedName = equalsIndex < 0 ? component : component[..equalsIndex];
            if (!TryDecodeQueryComponent(encodedName, out var decodedName) ||
                !string.Equals(decodedName, name, StringComparison.Ordinal))
            {
                continue;
            }

            var encodedValue = equalsIndex < 0 ? string.Empty : component[(equalsIndex + 1)..];
            if (TryDecodeQueryComponent(encodedValue, out var decodedValue))
            {
                values.Add(decodedValue);
            }
        }

        return values;
    }

    private static bool TryDecodeQueryComponent(string value, out string decoded)
    {
        try
        {
            decoded = Uri.UnescapeDataString(value.Replace('+', ' '));
            return true;
        }
        catch (UriFormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static bool JsonMatches(string? actual, JsonNode? expected)
    {
        if (actual is null)
        {
            return false;
        }

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(actual), expected);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
