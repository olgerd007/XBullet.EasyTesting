using System.Net;
using XBullet.EasyTesting.Diagnostics;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Http;

/// <summary>
/// A fluent, in-memory HTTP handler that returns arranged responses and records outbound requests.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<StubRule> _rules = [];
    private readonly List<StubHttpRequest> _requests = [];
    private readonly List<StubHttpExchange> _exchanges = [];

    /// <summary>Gets a stable copy of the requests received by this handler.</summary>
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
    public StubHttpResponseBuilder When(HttpMethod method, string requestUri)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return new StubHttpResponseBuilder(this, method, requestUri);
    }

    /// <summary>Verifies that an exact method-and-URI request was recorded the expected number of times.</summary>
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
    public StubHttpMessageHandler VerifyNotCalled(HttpMethod method, string requestUri) =>
        VerifyCalled(method, requestUri, expectedCount: 0);

    /// <summary>Verifies the number of recorded requests accepted by a custom predicate.</summary>
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
    public StubHttpMessageHandler Reset()
    {
        lock (_gate)
        {
            _rules.Clear();
            _requests.Clear();
            _exchanges.Clear();
        }

        return this;
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
            _rules.Add(new StubRule(
                method,
                requestUri,
                matchUriPathOnly,
                predicates,
                responseFactory));
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
        var headers = request.Headers
            .Concat(
                request.Content?.Headers ??
                Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(header => header.Value).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var capturedRequest = new StubHttpRequest(request.Method, request.RequestUri, headers, body);
        var exchange = new StubHttpExchange(capturedRequest);
        Func<StubHttpRequest, CancellationToken, Task<HttpResponseMessage>>? responseFactory;
        StubRuleMatchResult[] matchResults;
        lock (_gate)
        {
            _requests.Add(capturedRequest);
            _exchanges.Add(exchange);
            matchResults = _rules
                .Select(rule => rule.Evaluate(capturedRequest))
                .ToArray();
            responseFactory = matchResults
                .FirstOrDefault(result => result.IsMatch)
                ?.Rule.ResponseFactory;
        }

        try
        {
            var response = responseFactory is null
                ? new HttpResponseMessage(HttpStatusCode.NotImplemented)
                {
                    Content = new StringContent(
                        FormatMatchFailure(capturedRequest, matchResults))
                }
                : await responseFactory(capturedRequest, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The outbound HTTP response factory returned null.");
            response.RequestMessage ??= request;

            var responseHeaders = response.Headers
                .Concat(
                    response.Content?.Headers ??
                    Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.SelectMany(header => header.Value).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
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

            var failures = new List<string>();
            foreach (var predicate in Predicates)
            {
                try
                {
                    if (!predicate.Matches(request))
                    {
                        failures.Add($"did not satisfy {predicate.Description}");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{predicate.Description} threw {exception.GetType().Name}: {exception.Message}");
                }
            }

            return new StubRuleMatchResult(this, failures);
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
    protected override void Dispose(bool disposing)
    {
    }
}
