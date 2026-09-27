namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates an API-key-shaped test principal.</summary>
/// <remarks>
/// This builder creates simulated claims and never stores or validates an API-key secret. It is
/// mutable and not thread-safe; each call to <see cref="Build"/> returns a snapshot.
/// </remarks>
public sealed class TestApiKeyBuilder
{
    /// <summary>Claim containing the non-secret identifier of the represented API key.</summary>
    public const string KeyIdClaim = "api_key_id";

    private readonly List<string> _roles = [];
    private readonly List<TestClaim> _claims = [];
    private readonly string _authenticationScheme;
    private string _keyId = Guid.NewGuid().ToString("N");
    private string _clientName = "api-key-test-client";

    /// <summary>Creates an API-key test builder targeting the supplied application scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty simulated scheme targeted by the resulting user. When omitted, the default
    /// XBullet test-authentication scheme is used.
    /// </param>
    public TestApiKeyBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the non-secret API-key identifier included in the claims principal.</summary>
    /// <param name="keyId">
    /// The non-empty identifier used for the <see cref="KeyIdClaim"/> and name-identifier claims.
    /// It is metadata, not an API-key secret.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestApiKeyBuilder WithKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        _keyId = keyId;
        return this;
    }

    /// <summary>Sets the client name exposed through <see cref="System.Security.Principal.IIdentity.Name"/>.</summary>
    /// <param name="clientName">The non-empty client-name claim value.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestApiKeyBuilder WithClientName(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clientName = clientName;
        return this;
    }

    /// <summary>Adds a role recognized by ASP.NET Core role authorization.</summary>
    /// <param name="role">The non-empty role value to append. Duplicate roles are retained.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestApiKeyBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds roles recognized by ASP.NET Core role authorization.</summary>
    /// <param name="roles">
    /// The non-null role array appended in order. Every element must be non-empty; duplicates are
    /// retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestApiKeyBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            WithRole(role);
        }

        return this;
    }

    /// <summary>Adds a custom claim associated with the API key.</summary>
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestApiKeyBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the API-key-shaped test user.</summary>
    /// <returns>
    /// A new user containing snapshots of the configured key identifier, client name, roles, and
    /// custom claims. Later builder changes do not modify the returned user.
    /// </returns>
    public TestUser Build()
    {
        var claims = new List<TestClaim>
        {
            new(KeyIdClaim, _keyId),
            new(
                TestAuthenticationDefaults.AuthenticationMethodClaim,
                TestAuthenticationDefaults.ApiKeyAuthenticationMethod)
        };
        claims.AddRange(_claims);

        return new TestUser
        {
            AuthenticationScheme = _authenticationScheme,
            AuthenticationType = TestAuthenticationDefaults.ApiKeyAuthenticationType,
            NameIdentifier = _keyId,
            Name = _clientName,
            Roles = _roles.ToArray(),
            Claims = claims.ToArray()
        };
    }
}
