using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently defines a JWT produced by a <see cref="TestJwtAuthority"/>.</summary>
/// <remarks>
/// This mutable builder is not thread-safe. Unless overridden, the issuer, audience, and lifetime
/// come from the authority; the subject is a new identifier, the name is
/// <c>integration-test-user</c>, and the not-before time is the build time. Each token also receives
/// a new JWT identifier when it is built.
/// </remarks>
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
    /// <param name="issuer">
    /// The non-empty issuer claim. The value is used as supplied and is not normalized or validated
    /// as a URI.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        _issuer = issuer;
        return this;
    }

    /// <summary>Replaces the token audiences with one value.</summary>
    /// <param name="audience">
    /// The non-empty audience claim. This removes every audience previously added to the builder.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="audience"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        _audiences.Clear();
        _audiences.Add(audience);
        return this;
    }

    /// <summary>Adds a token audience.</summary>
    /// <param name="audience">
    /// The non-empty audience claim to append. Duplicate audience values are retained.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="audience"/> is empty or whitespace.</exception>
    public TestJwtBuilder AddAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        _audiences.Add(audience);
        return this;
    }

    /// <summary>Sets the subject claim.</summary>
    /// <param name="subject">
    /// The non-empty subject identifier. By default, each builder uses a new 32-character GUID
    /// without separators.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="subject"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the name claim.</summary>
    /// <param name="name">
    /// The non-empty value for the <c>name</c> claim. The default is
    /// <c>integration-test-user</c>.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Adds a delegated OAuth scope.</summary>
    /// <param name="scope">
    /// The non-empty scope value to append. Duplicate values are retained; all scopes are emitted
    /// in one space-delimited <c>scp</c> claim.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scopes.Add(scope);
        return this;
    }

    /// <summary>Adds several delegated OAuth scopes.</summary>
    /// <param name="scopes">
    /// The non-null scope array to append in order. Every element must be non-empty; duplicate
    /// values are retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An element is empty or whitespace.</exception>
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
    /// <param name="role">
    /// The non-empty role value to append as a <c>roles</c> claim. Duplicate roles are retained.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="role"/> is empty or whitespace.</exception>
    public TestJwtBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds several application roles.</summary>
    /// <param name="roles">
    /// The non-null role array to append in order. Every element must be non-empty; duplicate roles
    /// are retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roles"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An element is empty or whitespace.</exception>
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
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public TestJwtBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Sets the token's not-before time.</summary>
    /// <param name="notBefore">
    /// The absolute instant at which the token becomes valid. Its offset is converted to UTC when
    /// the token is serialized. The default is the build time.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    public TestJwtBuilder NotBefore(DateTimeOffset notBefore)
    {
        _notBefore = notBefore;
        return this;
    }

    /// <summary>Sets the token's absolute expiry.</summary>
    /// <param name="expires">
    /// The absolute expiration instant. Its offset is converted to UTC when the token is serialized.
    /// No ordering check against the not-before time is performed by this method.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    public TestJwtBuilder ExpiresAt(DateTimeOffset expires)
    {
        _expires = expires;
        return this;
    }

    /// <summary>Sets the token expiration relative to the current time.</summary>
    /// <param name="lifetime">
    /// The positive duration added to UTC now when this method is called. This sets an absolute
    /// expiration and does not defer the calculation until token serialization.
    /// </param>
    /// <returns>This builder so additional token values can be configured.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lifetime"/> is zero or negative.
    /// </exception>
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
