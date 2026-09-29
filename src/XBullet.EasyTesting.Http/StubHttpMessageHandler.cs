using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using XBullet.EasyTesting.Diagnostics;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Http;

/// <summary>
/// A fluent, in-memory HTTP handler that returns arranged responses and records outbound requests.
/// </summary>
/// <remarks>
/// Rule registration, request recording, snapshots, verification, and reset are safe to call from
/// multiple threads. User-supplied matcher predicates and response factories can run concurrently.
/// Raw recorded requests are not redacted and can contain sensitive values.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly StubHttpMessageHandlerOptions _options;
    private StubRule[] _rules = [];
    private readonly Queue<StubHttpRequest> _requests = [];
    private readonly Queue<StubHttpExchange> _exchanges = [];
    private int _callCount;

    /// <summary>Creates a handler with unlimited exchange and body capture.</summary>
    public StubHttpMessageHandler()
        : this(new StubHttpMessageHandlerOptions())
    {
    }

    /// <summary>Creates a handler with explicit recording and body-capture limits.</summary>
    /// <param name="options">
    /// The non-null immutable option values retained by this handler.
    /// </param>
    public StubHttpMessageHandler(StubHttpMessageHandlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    /// <summary>Gets a stable copy of the requests received by this handler.</summary>
    /// <value>
    /// A newly allocated array containing the recorded request objects in arrival order. Later
    /// requests and resets do not change the array. The handler owns the records and their nested
    /// header values; callers must not mutate them. Values are not redacted.
    /// </value>
    public IReadOnlyList<StubHttpRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Gets a stable copy of the request/response exchanges observed by this handler.</summary>
    /// <value>
    /// A newly allocated array containing the handler-owned exchange objects in arrival order.
    /// Later requests and resets do not change the array, but an exchange's response body state may
    /// update when the HTTP caller subsequently consumes its content.
    /// </value>
    public IReadOnlyList<StubHttpExchange> Exchanges
    {
        get
        {
            lock (_gate)
            {
                return _exchanges.ToArray();
            }
        }
    }

    /// <summary>Gets the number of requests received since construction or the last reset.</summary>
    /// <value>The current request count as a thread-safe point-in-time value.</value>
    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _callCount;
            }
        }
    }

    /// <summary>Starts an exact method-and-URI response rule.</summary>
    /// <param name="method">
    /// The non-null HTTP method to match. The handler reads but does not own or mutate it.
    /// </param>
    /// <param name="requestUri">
    /// The non-empty URI text to match. HTTP and HTTPS absolute URIs match the full absolute URI;
    /// other values match a relative request's original text or an absolute request's path and
    /// query. Adding a query-parameter matcher later changes comparison to the path only.
    /// </param>
    /// <returns>A new mutable response builder associated with this handler.</returns>
    public StubHttpResponseBuilder When(HttpMethod method, string requestUri)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return new StubHttpResponseBuilder(this, method, requestUri);
    }

    /// <summary>Verifies that an exact method-and-URI request was recorded the expected number of times.</summary>
    /// <param name="method">
    /// The non-null HTTP method to match. The handler reads but does not own or mutate it.
    /// </param>
    /// <param name="requestUri">
    /// The non-empty URI text compared using the same exact absolute-or-relative rules as
    /// <see cref="When"/> before additional matchers are applied.
    /// </param>
    /// <param name="expectedCount">
    /// The non-negative number of matching requests required. The default is one.
    /// </param>
    /// <returns>This handler so additional verification calls can be chained.</returns>
    /// <exception cref="StubHttpVerificationException">
    /// The point-in-time count does not equal <paramref name="expectedCount"/>. Recognized
    /// sensitive query values are redacted from the exception message.
    /// </exception>
    public StubHttpMessageHandler VerifyCalled(
        HttpMethod method,
        string requestUri,
        int expectedCount = 1)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);

        StubHttpRequest[] requests;
        lock (_gate)
        {
            requests = _requests.ToArray();
        }

        var uriMatcher = StubUriMatcher.Create(requestUri, matchUriPathOnly: false);
        var actualCount = requests.Count(request =>
            method == request.Method && uriMatcher.Matches(request.RequestUri));
        if (actualCount != expectedCount)
        {
            throw new StubHttpVerificationException(
                $"Expected {method} {UriDiagnosticFormatter.Format(requestUri)} to be called " +
                $"{expectedCount} time(s), " +
                $"but it was called {actualCount} time(s).{Environment.NewLine}" +
                FormatRecordedRequests(requests));
        }

        return this;
    }

    /// <summary>Verifies that an exact method-and-URI request was not recorded.</summary>
    /// <param name="method">
    /// The non-null HTTP method to match. The handler reads but does not own or mutate it.
    /// </param>
    /// <param name="requestUri">
    /// The non-empty URI text compared using the same exact absolute-or-relative rules as
    /// <see cref="When"/> before additional matchers are applied.
    /// </param>
    /// <returns>This handler so additional verification calls can be chained.</returns>
    /// <exception cref="StubHttpVerificationException">A matching request was recorded.</exception>
    public StubHttpMessageHandler VerifyNotCalled(HttpMethod method, string requestUri) =>
        VerifyCalled(method, requestUri, expectedCount: 0);

    /// <summary>Verifies the number of recorded requests accepted by a custom predicate.</summary>
    /// <param name="predicate">
    /// The non-null predicate invoked once, sequentially, for each request in a point-in-time
    /// snapshot. The handler owns each supplied request; the predicate must not mutate it.
    /// Exceptions from the predicate are propagated unchanged.
    /// </param>
    /// <param name="expectedCount">The non-negative number of accepted requests required.</param>
    /// <param name="description">
    /// Text used in a failed verification message. When <see langword="null"/>, empty, or
    /// whitespace, <c>the request predicate</c> is used. Do not include secrets in this value.
    /// </param>
    /// <returns>This handler so additional verification calls can be chained.</returns>
    /// <exception cref="StubHttpVerificationException">
    /// The point-in-time accepted count does not equal <paramref name="expectedCount"/>.
    /// </exception>
    public StubHttpMessageHandler Verify(
        Func<StubHttpRequest, bool> predicate,
        int expectedCount,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);

        StubHttpRequest[] requests;
        lock (_gate)
        {
            requests = _requests.ToArray();
        }

        var actualCount = requests.Count(predicate);
        if (actualCount != expectedCount)
        {
            var expectation = string.IsNullOrWhiteSpace(description)
                ? "the request predicate"
                : description;
            throw new StubHttpVerificationException(
                $"Expected {expectation} to match {expectedCount} request(s), " +
                $"but it matched {actualCount}.{Environment.NewLine}" +
                FormatRecordedRequests(requests));
        }

        return this;
    }

    /// <summary>Removes all arranged responses and recorded requests.</summary>
    /// <returns>
    /// This handler for reuse. Requests or rules added concurrently after the reset lock is
    /// released are retained.
    /// </returns>
    public StubHttpMessageHandler Reset()
    {
        lock (_gate)
        {
            Volatile.Write(ref _rules, []);
            _requests.Clear();
            _exchanges.Clear();
            _callCount = 0;
        }

        return this;
    }

    /// <summary>Removes all arranged responses and recorded exchanges.</summary>
    /// <param name="cancellationToken">
    /// Cancels the reset before any state is changed. Once cancellation is checked, reset completes
    /// synchronously. The default token does not request cancellation.
    /// </param>
    /// <returns>A value task that is already complete after the reset finishes.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <summary>Captures recorded requests for scenario-failure diagnostics.</summary>
    /// <param name="cancellationToken">
    /// Cancels capture before a snapshot is created. The default token does not request
    /// cancellation.
    /// </param>
    /// <returns>
    /// An already-completed value task containing a newly allocated serializable snapshot with the
    /// call count and requests. Recognized sensitive query values are redacted from request URIs;
    /// captured headers and bodies are included unchanged and can contain sensitive data.
    /// </returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StubHttpRequest[] requests;
        int callCount;
        lock (_gate)
        {
            requests = _requests.ToArray();
            callCount = _callCount;
        }

        return ValueTask.FromResult<object?>(new
        {
            CallCount = callCount,
            Requests = requests.Select(request => new
            {
                request.Method,
                RequestUri = UriDiagnosticFormatter.Format(request.RequestUri),
                request.Headers,
                request.Body
            }).ToArray()
        });
    }

    internal StubHttpMessageHandler AddRule(
        HttpMethod method,
        string requestUri,
        bool matchUriPathOnly,
        IReadOnlyList<StubRequestPredicate> predicates,
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        lock (_gate)
        {
            var rules = _rules;
            var updatedRules = new StubRule[rules.Length + 1];
            Array.Copy(rules, updatedRules, rules.Length);
            updatedRules[^1] = new StubRule(
                method,
                requestUri,
                StubUriMatcher.Create(requestUri, matchUriPathOnly),
                predicates,
                responseFactory);
            Volatile.Write(ref _rules, updatedRules);
        }

        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var (body, bodyTruncated) = request.Content is null || !_options.CaptureRequestBodies
            ? (null, false)
            : await CaptureRequestBodyAsync(
                request.Content,
                _options.MaximumRequestBodyBytes,
                cancellationToken);
        var headers = CaptureHeaders(request.Headers, request.Content?.Headers);

        var capturedRequest = new StubHttpRequest(request.Method, request.RequestUri, headers, body)
        {
            BodyTruncated = bodyTruncated
        };
        var exchange = new StubHttpExchange(capturedRequest);
        lock (_gate)
        {
            _callCount++;
            if (_options.MaximumRecordedExchanges != 0)
            {
                _requests.Enqueue(capturedRequest);
                _exchanges.Enqueue(exchange);
                if (_options.MaximumRecordedExchanges is int maximum)
                {
                    while (_requests.Count > maximum)
                    {
                        _requests.Dequeue();
                        _exchanges.Dequeue();
                    }
                }
            }
        }

        var rules = Volatile.Read(ref _rules);
        List<StubRuleMatchResult>? matchResults = null;
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>? responseFactory = null;
        using (var matchContext = new StubRequestMatchContext(capturedRequest))
        {
            foreach (var rule in rules)
            {
                var matchResult = rule.Evaluate(matchContext);
                if (matchResult.IsMatch)
                {
                    responseFactory = rule.ResponseFactory;
                    break;
                }

                (matchResults ??= []).Add(matchResult);
            }
        }

        try
        {
            var response = responseFactory is null
                ? new HttpResponseMessage(HttpStatusCode.NotImplemented)
                {
                    Content = new StringContent(
                        FormatMatchFailure(capturedRequest, matchResults ?? []))
                }
                : await responseFactory(capturedRequest, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The outbound HTTP response factory returned null.");
            response.RequestMessage ??= request;

            var responseHeaders = CaptureHeaders(response.Headers, response.Content?.Headers);
            exchange.SetResponse(new StubHttpResponse(
                (int)response.StatusCode,
                response.ReasonPhrase,
                responseHeaders,
                response.Content is null,
                ReadOnlyMemory<byte>.Empty,
                null));

            if (response.Content is not null && _options.CaptureResponseBodies)
            {
                response.Content = new RecordingHttpContent(
                    response.Content,
                    _options.MaximumResponseBodyBytes,
                    exchange.SetResponseBody);
            }

            return response;
        }
        catch (Exception exception)
        {
            exchange.SetFailure(exception);
            throw;
        }
    }

    private static async ValueTask<(string Body, bool Truncated)> CaptureRequestBodyAsync(
        HttpContent content,
        int? maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes is null)
        {
            return (await content.ReadAsStringAsync(cancellationToken), false);
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var capture = new MemoryStream(Math.Min(maximumBytes.Value, 81_920));
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        var truncated = false;
        try
        {
            while (capture.Length <= maximumBytes.Value)
            {
                var remaining = maximumBytes.Value - (int)capture.Length;
                var requestedBytes = remaining == int.MaxValue
                    ? buffer.Length
                    : Math.Min(buffer.Length, remaining + 1);
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, requestedBytes),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                if (bytesRead > remaining)
                {
                    capture.Write(buffer, 0, remaining);
                    truncated = true;
                    break;
                }

                capture.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (DecodeBody(capture.GetBuffer().AsSpan(0, (int)capture.Length), content), truncated);
    }

    private static string DecodeBody(ReadOnlySpan<byte> bytes, HttpContent content)
    {
        var encoding = Encoding.UTF8;
        var preambleLength = 0;
        if (bytes.StartsWith(Encoding.UTF32.Preamble))
        {
            encoding = Encoding.UTF32;
            preambleLength = Encoding.UTF32.Preamble.Length;
        }
        else if (bytes.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = Encoding.BigEndianUnicode.Preamble.Length;
        }
        else if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            encoding = Encoding.Unicode;
            preambleLength = Encoding.Unicode.Preamble.Length;
        }
        else if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            preambleLength = Encoding.UTF8.Preamble.Length;
        }
        else if (content.Headers.ContentType?.CharSet is string characterSet)
        {
            try
            {
                encoding = Encoding.GetEncoding(characterSet.Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }

        return encoding.GetString(bytes[preambleLength..]);
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(
        HttpHeaders messageHeaders,
        HttpHeaders? contentHeaders)
    {
        var captured = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddHeaders(messageHeaders);
        if (contentHeaders is not null)
        {
            AddHeaders(contentHeaders);
        }

        return captured;

        void AddHeaders(HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                var values = header.Value.ToArray();
                if (!captured.TryGetValue(header.Key, out var existingValues))
                {
                    captured.Add(header.Key, values);
                    continue;
                }

                var combinedValues = new string[existingValues.Length + values.Length];
                existingValues.CopyTo(combinedValues, 0);
                values.CopyTo(combinedValues, existingValues.Length);
                captured[header.Key] = combinedValues;
            }
        }
    }

    private sealed record StubRule(
        HttpMethod Method,
        string RequestUri,
        StubUriMatcher UriMatcher,
        IReadOnlyList<StubRequestPredicate> Predicates,
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> ResponseFactory)
    {
        public StubRuleMatchResult Evaluate(StubRequestMatchContext context)
        {
            var request = context.Request;
            if (Method != request.Method)
            {
                return new StubRuleMatchResult(this, StubRuleMismatch.Method, null);
            }

            if (!UriMatcher.Matches(request.RequestUri))
            {
                return new StubRuleMatchResult(this, StubRuleMismatch.Uri, null);
            }

            List<string>? failures = null;
            foreach (var predicate in Predicates)
            {
                try
                {
                    if (!predicate.Matches(context))
                    {
                        (failures ??= []).Add($"did not satisfy {predicate.Description}");
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(
                        $"{predicate.Description} threw {exception.GetType().Name}: {exception.Message}");
                }
            }

            return failures is null
                ? new StubRuleMatchResult(this, StubRuleMismatch.None, null)
                : new StubRuleMatchResult(this, StubRuleMismatch.Predicate, failures);
        }
    }

    private readonly record struct StubRuleMatchResult(
        StubRule Rule,
        StubRuleMismatch Mismatch,
        IReadOnlyList<string>? PredicateFailures)
    {
        public bool IsMatch => Mismatch == StubRuleMismatch.None;

        public string Describe(StubHttpRequest request) => Mismatch switch
        {
            StubRuleMismatch.Method =>
                $"method differed: expected {Rule.Method}, received {request.Method}",
            StubRuleMismatch.Uri =>
                $"URI differed: expected {UriDiagnosticFormatter.Format(Rule.RequestUri)}, " +
                $"received {UriDiagnosticFormatter.Format(request.RequestUri)}",
            StubRuleMismatch.Predicate => string.Join("; ", PredicateFailures!),
            _ => string.Empty
        };
    }

    private enum StubRuleMismatch
    {
        None,
        Method,
        Uri,
        Predicate
    }

    private sealed record StubUriMatcher(string ExpectedValue, bool IsAbsolute, bool MatchPathOnly)
    {
        public static StubUriMatcher Create(string requestUri, bool matchUriPathOnly)
        {
            if (Uri.TryCreate(requestUri, UriKind.Absolute, out var configuredUri) &&
                (string.Equals(
                     configuredUri.Scheme,
                     Uri.UriSchemeHttp,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     configuredUri.Scheme,
                     Uri.UriSchemeHttps,
                     StringComparison.OrdinalIgnoreCase)))
            {
                return new StubUriMatcher(
                    matchUriPathOnly
                        ? configuredUri.GetLeftPart(UriPartial.Path)
                        : configuredUri.AbsoluteUri,
                    IsAbsolute: true,
                    matchUriPathOnly);
            }

            return new StubUriMatcher(
                matchUriPathOnly ? GetPath(requestUri) : requestUri,
                IsAbsolute: false,
                matchUriPathOnly);
        }

        public bool Matches(Uri? requestUri)
        {
            if (requestUri is null || (IsAbsolute && !requestUri.IsAbsoluteUri))
            {
                return false;
            }

            var actualValue = IsAbsolute
                ? MatchPathOnly
                    ? requestUri.GetLeftPart(UriPartial.Path)
                    : requestUri.AbsoluteUri
                : requestUri.IsAbsoluteUri
                    ? requestUri.PathAndQuery
                    : requestUri.OriginalString;
            if (!IsAbsolute && MatchPathOnly)
            {
                actualValue = GetPath(actualValue);
            }

            return string.Equals(actualValue, ExpectedValue, StringComparison.Ordinal);
        }
    }

    private static string GetPath(string uri)
    {
        var queryOrFragmentIndex = uri.IndexOfAny(['?', '#']);
        return queryOrFragmentIndex < 0 ? uri : uri[..queryOrFragmentIndex];
    }

    private static string FormatRecordedRequests(IReadOnlyCollection<StubHttpRequest> requests) =>
        requests.Count == 0
            ? "No requests were recorded."
            : "Recorded requests:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                requests.Select(request =>
                    $"- {request.Method} {UriDiagnosticFormatter.Format(request.RequestUri)}"));

    private static string FormatMatchFailure(
        StubHttpRequest request,
        IReadOnlyList<StubRuleMatchResult> matchResults)
    {
        var message =
            $"No outbound HTTP stub matches {request.Method} " +
            $"{UriDiagnosticFormatter.Format(request.RequestUri)}.";
        if (matchResults.Count == 0)
        {
            return $"{message}{Environment.NewLine}No rules were configured.";
        }

        return message + Environment.NewLine + "Configured rule mismatches:" + Environment.NewLine +
            string.Join(
                Environment.NewLine,
                matchResults.Select((result, index) =>
                    $"- Rule {index + 1} ({result.Rule.Method} " +
                    $"{UriDiagnosticFormatter.Format(result.Rule.RequestUri)}): " +
                    result.Describe(request)));
    }

    /// <summary>
    /// Keeps this in-memory handler reusable when a scenario-specific service provider disposes its
    /// HTTP pipeline. The handler owns no operating-system resources; call <see cref="Reset"/> to clear it.
    /// </summary>
    /// <param name="disposing">
    /// Ignored. Both explicit disposal and finalization leave the in-memory handler usable and do
    /// not clear its rules or recordings.
    /// </param>
    protected override void Dispose(bool disposing)
    {
    }
}
