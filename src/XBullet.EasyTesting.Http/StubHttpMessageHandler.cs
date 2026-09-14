using System.Net;

namespace XBullet.EasyTesting.Http;

/// <summary>
/// A fluent, in-memory HTTP handler that returns arranged responses and records outbound requests.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<StubRule> _rules = [];
    private readonly List<StubHttpRequest> _requests = [];

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

    /// <summary>Removes all arranged responses and recorded requests.</summary>
    public StubHttpMessageHandler Reset()
    {
        lock (_gate)
        {
            _rules.Clear();
            _requests.Clear();
        }

        return this;
    }

    internal StubHttpMessageHandler AddRule(
        HttpMethod method,
        string requestUri,
        Func<HttpResponseMessage> responseFactory)
    {
        lock (_gate)
        {
            _rules.Add(new StubRule(method, requestUri, responseFactory));
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

        Func<HttpResponseMessage>? responseFactory;
        lock (_gate)
        {
            _requests.Add(new StubHttpRequest(request.Method, request.RequestUri, headers, body));
            responseFactory = _rules
                .FirstOrDefault(rule => rule.Matches(request))
                ?.ResponseFactory;
        }

        var response = responseFactory?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotImplemented)
        {
            Content = new StringContent(
                $"No outbound HTTP stub matches {request.Method} {request.RequestUri?.PathAndQuery}.")
        };
        response.RequestMessage ??= request;
        return response;
    }

    private sealed record StubRule(
        HttpMethod Method,
        string RequestUri,
        Func<HttpResponseMessage> ResponseFactory)
    {
        public bool Matches(HttpRequestMessage request)
        {
            if (Method != request.Method || request.RequestUri is null)
            {
                return false;
            }

            if (Uri.TryCreate(RequestUri, UriKind.Absolute, out _))
            {
                return request.RequestUri.IsAbsoluteUri &&
                    string.Equals(request.RequestUri.AbsoluteUri, RequestUri, StringComparison.Ordinal);
            }

            var actualUri = request.RequestUri.IsAbsoluteUri
                ? request.RequestUri.PathAndQuery
                : request.RequestUri.OriginalString;
            return string.Equals(actualUri, RequestUri, StringComparison.Ordinal);
        }
    }
}
