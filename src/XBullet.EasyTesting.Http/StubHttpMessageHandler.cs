using System.Net;
using System.Net.Http.Headers;
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
    private StubRule[] _rules = [];
    private readonly List<StubHttpRequest> _requests = [];
    private readonly List<StubHttpExchange> _exchanges = [];

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
                return _requests.Count;
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

        var actualCount = requests.Count(request => MatchesMethodAndUri(method, requestUri, request));
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
        lock (_gate)
        {
            requests = _requests.ToArray();
        }

        return ValueTask.FromResult<object?>(new
        {
            CallCount = requests.Length,
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
                matchUriPathOnly,
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
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = CaptureHeaders(request.Headers, request.Content?.Headers);

        var capturedRequest = new StubHttpRequest(request.Method, request.RequestUri, headers, body);
        var exchange = new StubHttpExchange(capturedRequest);
        lock (_gate)
        {
            _requests.Add(capturedRequest);
            _exchanges.Add(exchange);
        }

        var rules = Volatile.Read(ref _rules);
        List<StubRuleMatchResult>? matchResults = null;
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>? responseFactory = null;
        foreach (var rule in rules)
        {
            var matchResult = rule.Evaluate(capturedRequest);
            if (matchResult.IsMatch)
            {
                responseFactory = rule.ResponseFactory;
                break;
            }

            (matchResults ??= []).Add(matchResult);
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

            if (response.Content is not null)
            {
                response.Content = new RecordingHttpContent(
                    response.Content,
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
        bool MatchUriPathOnly,
        IReadOnlyList<StubRequestPredicate> Predicates,
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>> ResponseFactory)
    {
        public StubRuleMatchResult Evaluate(StubHttpRequest request)
        {
            if (Method != request.Method)
            {
                return new StubRuleMatchResult(
                    this,
                    [$"method differed: expected {Method}, received {request.Method}"]);
            }

            if (!MatchesMethodAndUri(Method, RequestUri, request, MatchUriPathOnly))
            {
                return new StubRuleMatchResult(
                    this,
                    [$"URI differed: expected {UriDiagnosticFormatter.Format(RequestUri)}, " +
                     $"received {UriDiagnosticFormatter.Format(request.RequestUri)}"]);
            }

            List<string>? failures = null;
            foreach (var predicate in Predicates)
            {
                try
                {
                    if (!predicate.Matches(request))
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

            return new StubRuleMatchResult(this, failures ?? []);
        }
    }

    private sealed record StubRuleMatchResult(StubRule Rule, IReadOnlyList<string> Failures)
    {
        public bool IsMatch => Failures.Count == 0;
    }

    private static bool MatchesMethodAndUri(
        HttpMethod method,
        string requestUri,
        StubHttpRequest request,
        bool matchUriPathOnly = false)
    {
        if (method != request.Method || request.RequestUri is null)
        {
            return false;
        }

        if (Uri.TryCreate(requestUri, UriKind.Absolute, out var configuredUri) &&
            (string.Equals(configuredUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(configuredUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            if (!request.RequestUri.IsAbsoluteUri)
            {
                return false;
            }

            var configuredValue = matchUriPathOnly
                ? configuredUri.GetLeftPart(UriPartial.Path)
                : configuredUri.AbsoluteUri;
            var actualValue = matchUriPathOnly
                ? request.RequestUri.GetLeftPart(UriPartial.Path)
                : request.RequestUri.AbsoluteUri;
            return string.Equals(actualValue, configuredValue, StringComparison.Ordinal);
        }

        var actualUri = request.RequestUri.IsAbsoluteUri
            ? request.RequestUri.PathAndQuery
            : request.RequestUri.OriginalString;
        if (matchUriPathOnly)
        {
            actualUri = GetPath(actualUri);
            requestUri = GetPath(requestUri);
        }

        return string.Equals(actualUri, requestUri, StringComparison.Ordinal);
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
                    string.Join("; ", result.Failures)));
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
