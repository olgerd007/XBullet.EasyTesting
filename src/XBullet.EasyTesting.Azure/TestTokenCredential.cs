using Azure.Core;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Azure;

/// <summary>A deterministic Azure credential that records token requests.</summary>
public sealed class TestTokenCredential : TokenCredential, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<TestTokenRequest> _requests = [];
    private string _token;
    private DateTimeOffset _expiresOn;
    private Exception? _exception;

    /// <summary>Creates a successful credential with a deterministic token and expiry.</summary>
    /// <param name="token">
    /// The non-empty, non-whitespace token returned by synchronous and asynchronous requests. The
    /// default is <c>xbullet-test-token</c>. Treat custom values as secrets.
    /// </param>
    /// <param name="expiresOn">
    /// The absolute token expiry, or <see langword="null"/> to use January 1, 2100 at 00:00 UTC.
    /// </param>
    public TestTokenCredential(
        string token = "xbullet-test-token",
        DateTimeOffset? expiresOn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
        _expiresOn = expiresOn ?? new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Gets a stable copy of captured token requests.</summary>
    /// <value>
    /// A newly allocated snapshot in request order. The caller may retain the list. Individual
    /// requests can contain unredacted claims and identifiers that should be treated as sensitive.
    /// </value>
    public IReadOnlyList<TestTokenRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Gets the number of captured token requests.</summary>
    /// <value>The total number of successful and arranged-failure requests recorded since construction or reset.</value>
    public int RequestCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    /// <summary>Configures the credential to return a token and clears any arranged failure.</summary>
    /// <param name="token">The non-empty, non-whitespace token to return. Treat the value as a secret.</param>
    /// <param name="expiresOn">
    /// The new absolute expiry, or <see langword="null"/> to preserve the currently configured expiry.
    /// </param>
    /// <returns>
    /// This credential, for chaining. The arranged failure is cleared; recorded requests are preserved.
    /// </returns>
    public TestTokenCredential SucceedWith(string token, DateTimeOffset? expiresOn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        lock (_gate)
        {
            _token = token;
            if (expiresOn.HasValue)
            {
                _expiresOn = expiresOn.Value;
            }

            _exception = null;
        }

        return this;
    }

    /// <summary>Configures the credential to throw for subsequent token requests.</summary>
    /// <param name="exception">
    /// The non-null exception instance retained and thrown after each subsequent request is recorded.
    /// The same instance is used until successful behavior is configured.
    /// </param>
    /// <returns>This credential, for chaining. The configured token and recorded requests are preserved.</returns>
    public TestTokenCredential FailWith(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            _exception = exception;
        }

        return this;
    }

    /// <summary>Clears captured requests while preserving the arranged credential behavior.</summary>
    /// <returns>
    /// This credential, for chaining. The token, expiry, and any arranged failure remain unchanged.
    /// </returns>
    public TestTokenCredential Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
        }

        return this;
    }

    /// <inheritdoc />
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RecordAndCreateToken(requestContext);
    }

    /// <inheritdoc />
    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RecordAndCreateToken(requestContext));
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
            request.Scopes,
            request.ParentRequestId,
            request.TenantId,
            HasClaims = request.Claims is not null
        }).ToArray();
        return ValueTask.FromResult<object?>(new
        {
            RequestCount,
            Requests = requests
        });
    }

    private AccessToken RecordAndCreateToken(TokenRequestContext context)
    {
        lock (_gate)
        {
            _requests.Add(new TestTokenRequest(
                context.Scopes.ToArray(),
                context.ParentRequestId,
                context.Claims,
                context.TenantId));
            if (_exception is not null)
            {
                throw _exception;
            }

            return new AccessToken(_token, _expiresOn);
        }
    }
}
