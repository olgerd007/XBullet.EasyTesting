using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently defines a locally signed JWT.</summary>
public sealed class TestJwtBuilder
{
    private readonly TestJwtAuthority _authority;
    private readonly List<string> _audiences = [];
    private readonly List<string> _scopes = [];
    private readonly List<string> _roles = [];
    private readonly List<TestClaim> _claims = [];
    private string? _issuer;
    private string _subject = Guid.NewGuid().ToString("N");
    private string _name = "integration-test-user";
    private DateTimeOffset? _notBefore;
    private DateTimeOffset? _expires;

    internal TestJwtBuilder(TestJwtAuthority authority)
    {
        _authority = authority;
    }

    /// <summary>Sets the token issuer.</summary>
    public TestJwtBuilder WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        _issuer = issuer;
        return this;
    }

    /// <summary>Replaces the token audiences.</summary>
    public TestJwtBuilder WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        _audiences.Clear();
        _audiences.Add(audience);
        return this;
    }

    /// <summary>Adds a token audience.</summary>
    public TestJwtBuilder AddAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        _audiences.Add(audience);
        return this;
    }

    /// <summary>Sets the subject claim.</summary>
    public TestJwtBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the name claim.</summary>
    public TestJwtBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Adds a delegated OAuth scope.</summary>
    public TestJwtBuilder WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scopes.Add(scope);
        return this;
    }

    /// <summary>Adds several delegated OAuth scopes.</summary>
    public TestJwtBuilder WithScopes(params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        foreach (var scope in scopes)
        {
            WithScope(scope);
        }

        return this;
    }

    /// <summary>Adds an application role.</summary>
    public TestJwtBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds several application roles.</summary>
    public TestJwtBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            WithRole(role);
        }

        return this;
    }

    /// <summary>Adds an arbitrary JWT claim.</summary>
    public TestJwtBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Sets the token's not-before time.</summary>
    public TestJwtBuilder NotBefore(DateTimeOffset notBefore)
    {
        _notBefore = notBefore;
        return this;
    }

    /// <summary>Sets the token's absolute expiry.</summary>
    public TestJwtBuilder ExpiresAt(DateTimeOffset expires)
    {
        _expires = expires;
        return this;
    }

    /// <summary>Sets the token lifetime relative to the time it is created.</summary>
    public TestJwtBuilder ExpiresAfter(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        _expires = DateTimeOffset.UtcNow.Add(lifetime);
        return this;
    }

    internal string Build()
    {
        var definition = CreateDefinition();
        return _authority.WriteToken(
            definition.Issuer,
            definition.Audiences,
            definition.Claims,
            definition.NotBefore,
            definition.Expires);
    }

    internal string Build(Microsoft.IdentityModel.Tokens.SigningCredentials signingCredentials)
    {
        var definition = CreateDefinition();
        return TestJwtAuthority.WriteToken(
            definition.Issuer,
            definition.Audiences,
            definition.Claims,
            definition.NotBefore,
            definition.Expires,
            signingCredentials);
    }

    internal string BuildUnsigned()
    {
        var definition = CreateDefinition();
        return _authority.WriteUnsignedToken(
            definition.Issuer,
            definition.Audiences,
            definition.Claims,
            definition.NotBefore,
            definition.Expires);
    }

    private TokenDefinition CreateDefinition()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, _subject),
            new("name", _name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(_claims.Select(claim => new Claim(claim.Type, claim.Value)));
        if (_scopes.Count > 0)
        {
            claims.Add(new Claim(AzureAdClaimTypes.Scope, string.Join(' ', _scopes)));
        }

        claims.AddRange(_roles.Select(role => new Claim(AzureAdClaimTypes.Roles, role)));
        return new TokenDefinition(
            _issuer ?? _authority.Options.Issuer,
            _audiences.Count == 0 ? [_authority.Options.Audience] : _audiences,
            claims,
            _notBefore ?? now,
            _expires ?? now.Add(_authority.Options.TokenLifetime));
    }

    private sealed record TokenDefinition(
        string Issuer,
        IEnumerable<string> Audiences,
        IEnumerable<Claim> Claims,
        DateTimeOffset NotBefore,
        DateTimeOffset Expires);
}
