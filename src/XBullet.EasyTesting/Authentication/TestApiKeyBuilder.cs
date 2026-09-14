namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates an API-key-shaped test principal.</summary>
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
    public TestApiKeyBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the non-secret API-key identifier included in the claims principal.</summary>
    public TestApiKeyBuilder WithKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        _keyId = keyId;
        return this;
    }

    /// <summary>Sets the client name exposed through <see cref="System.Security.Principal.IIdentity.Name"/>.</summary>
    public TestApiKeyBuilder WithClientName(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clientName = clientName;
        return this;
    }

    /// <summary>Adds a role recognized by ASP.NET Core role authorization.</summary>
    public TestApiKeyBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds roles recognized by ASP.NET Core role authorization.</summary>
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
    public TestApiKeyBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the API-key-shaped test user.</summary>
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
