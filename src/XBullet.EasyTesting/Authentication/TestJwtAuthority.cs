using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Creates locally signed JWTs and publishes their OIDC/JWKS metadata in a test host.</summary>
/// <remarks>
/// The authority owns its generated RSA keys and must be disposed by callers that create it with
/// <see cref="Create"/>. Authorities registered with a test factory are owned by that factory.
/// </remarks>
public sealed class TestJwtAuthority : IDisposable, ITestScenarioResource
{
    private readonly object _keysLock = new();
    private readonly List<SigningKey> _keys = [];
    private SigningKey _currentKey;
    private bool _disposed;
    private int _discoveryRequestCount;
    private int _jwksRequestCount;

    internal TestJwtAuthority(TestJwtAuthorityOptions options)
    {
        Options = options;
        _currentKey = CreateSigningKey(published: true);
        _keys.Add(_currentKey);
    }

    /// <summary>Gets this authority's configuration.</summary>
    /// <value>
    /// The live mutable options instance retained by the authority. It is not a snapshot; callers
    /// should finish configuration before using the authority and must not mutate it concurrently.
    /// </value>
    public TestJwtAuthorityOptions Options { get; }

    /// <summary>Gets the identifier of the key used to sign new tokens.</summary>
    /// <value>The current RSA signing key's non-empty identifier.</value>
    public string CurrentKeyId
    {
        get
        {
            lock (_keysLock)
            {
                return _currentKey.SecurityKey.KeyId;
            }
        }
    }

    /// <summary>Gets the number of discovery requests made through the JWT handler backchannel.</summary>
    /// <value>
    /// The request count since construction or the last reset. Direct calls to
    /// <see cref="GetDiscoveryDocument"/> are not counted.
    /// </value>
    public int DiscoveryRequestCount => Volatile.Read(ref _discoveryRequestCount);

    /// <summary>Gets the number of JWKS requests made through the JWT handler backchannel.</summary>
    /// <value>
    /// The request count since construction or the last reset. Direct calls to
    /// <see cref="GetJsonWebKeySet"/> are not counted.
    /// </value>
    public int JwksRequestCount => Volatile.Read(ref _jwksRequestCount);

    /// <summary>Creates a standalone local JWT authority.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new options instance before validation and
    /// key generation. When <see langword="null"/>, all documented option defaults are used. The
    /// callback and options argument are not retained separately or invoked concurrently.
    /// </param>
    /// <returns>
    /// A new authority that owns its generated signing keys. The caller owns and must dispose it.
    /// </returns>
    public static TestJwtAuthority Create(Action<TestJwtAuthorityOptions>? configure = null)
    {
        var options = new TestJwtAuthorityOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DiscoveryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.JwksPath);
        if (options.TokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.TokenLifetime));
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.ClockSkew));
        }

        return new TestJwtAuthority(options);
    }

    /// <summary>Creates a valid locally signed token.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new token builder before signing. When
    /// <see langword="null"/>, the authority's default issuer, audience, lifetime, and claims are
    /// used. The callback and builder are not retained or invoked concurrently.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string.</returns>
    public string CreateToken(Action<TestJwtBuilder>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var builder = new TestJwtBuilder(this);
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Creates a locally signed token that is already expired.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once after the not-before time is set to ten minutes ago
    /// and expiration to five minutes ago, but before signing. When <see langword="null"/>, those
    /// values remain unchanged; the callback can override either value.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string with the configured expiration.</returns>
    public string CreateExpiredToken(Action<TestJwtBuilder>? configure = null)
    {
        var now = DateTimeOffset.UtcNow;
        return CreateToken(builder =>
        {
            builder.NotBefore(now.AddMinutes(-10)).ExpiresAt(now.AddMinutes(-5));
            configure?.Invoke(builder);
        });
    }

    /// <summary>Creates a locally signed token with an invalid audience.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once after the audience is changed to the configured
    /// audience plus <c>-wrong</c>, but before signing. When <see langword="null"/>, that invalid
    /// audience remains; the callback can replace it.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string with the configured audience.</returns>
    public string CreateWrongAudienceToken(Action<TestJwtBuilder>? configure = null) =>
        CreateToken(builder =>
        {
            builder.WithAudience($"{Options.Audience}-wrong");
            configure?.Invoke(builder);
        });

    /// <summary>Creates a locally signed token with an invalid issuer.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once after the issuer is changed by appending
    /// <c>/wrong</c>, but before signing. When <see langword="null"/>, that invalid issuer remains;
    /// the callback can replace it.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string with the configured issuer.</returns>
    public string CreateWrongIssuerToken(Action<TestJwtBuilder>? configure = null) =>
        CreateToken(builder =>
        {
            builder.WithIssuer($"{Options.Issuer.TrimEnd('/')}/wrong");
            configure?.Invoke(builder);
        });

    /// <summary>Creates a token whose signature does not match its known key identifier.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new token builder before signing. When
    /// <see langword="null"/>, default claims are used. The token is signed with a temporary,
    /// untrusted RSA key carrying the current trusted key identifier; the callback cannot replace
    /// that key and is not retained.
    /// </param>
    /// <returns>A compact JWT string with a deliberately invalid RSA-SHA256 signature.</returns>
    public string CreateInvalidSignatureToken(Action<TestJwtBuilder>? configure = null) =>
        CreateUntrustedToken(useKnownKeyId: true, configure);

    /// <summary>Creates a signed token with a key identifier absent from JWKS.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new token builder before signing. When
    /// <see langword="null"/>, default claims are used. The token is signed with a temporary,
    /// untrusted RSA key whose identifier is not published; the callback cannot replace that key
    /// and is not retained.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string whose key is absent from this authority's JWKS.</returns>
    public string CreateUnknownKeyToken(Action<TestJwtBuilder>? configure = null) =>
        CreateUntrustedToken(useKnownKeyId: false, configure);

    /// <summary>Creates an unsigned token.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new token builder before serialization. When
    /// <see langword="null"/>, the authority's default issuer, audience, lifetime, and claims are
    /// used. The callback and builder are not retained or invoked concurrently.
    /// </param>
    /// <returns>A compact JWT string with no signing credentials.</returns>
    public string CreateUnsignedToken(Action<TestJwtBuilder>? configure = null)
    {
        var builder = new TestJwtBuilder(this);
        configure?.Invoke(builder);
        return builder.BuildUnsigned();
    }

    /// <summary>Creates a validly signed token that cannot be used until a future time.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once after the not-before time is set to five minutes in
    /// the future and expiration to ten minutes in the future, but before signing. When
    /// <see langword="null"/>, those values remain unchanged; the callback can override either one.
    /// </param>
    /// <returns>A compact, RSA-SHA256-signed JWT string with the configured validity interval.</returns>
    public string CreateFutureNotBeforeToken(Action<TestJwtBuilder>? configure = null)
    {
        var now = DateTimeOffset.UtcNow;
        return CreateToken(builder =>
        {
            builder.NotBefore(now.AddMinutes(5)).ExpiresAt(now.AddMinutes(10));
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Rotates the signing key and returns its identifier. Previous public keys can optionally remain in JWKS.
    /// </summary>
    /// <param name="retainPreviousKey">
    /// <see langword="true"/> to continue publishing all prior public keys so existing tokens can
    /// still be validated; <see langword="false"/> to publish only the new key. The default is
    /// <see langword="false"/>.
    /// </param>
    /// <returns>The non-empty identifier of the newly generated RSA signing key.</returns>
    public string RotateSigningKey(bool retainPreviousKey = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_keysLock)
        {
            if (!retainPreviousKey)
            {
                foreach (var key in _keys)
                {
                    key.Published = false;
                }
            }

            _currentKey = CreateSigningKey(published: true);
            _keys.Add(_currentKey);
            return _currentKey.SecurityKey.KeyId;
        }
    }

    /// <summary>Replaces all signing keys and clears the backchannel request counters.</summary>
    /// <param name="cancellationToken">
    /// Accepted for the resource contract but not observed because reset completes synchronously.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>A value task that is already complete after the reset side effects finish.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_keysLock)
        {
            foreach (var key in _keys)
            {
                key.Rsa.Dispose();
            }

            _keys.Clear();
            _currentKey = CreateSigningKey(published: true);
            _keys.Add(_currentKey);
        }

        Interlocked.Exchange(ref _discoveryRequestCount, 0);
        Interlocked.Exchange(ref _jwksRequestCount, 0);
        return ValueTask.CompletedTask;
    }

    /// <summary>Captures non-secret authority configuration and request counts.</summary>
    /// <param name="cancellationToken">
    /// Accepted for the resource contract but not observed because capture completes synchronously.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// An already-completed value task containing a newly allocated serializable snapshot with the
    /// issuer, audience, current key identifier, and backchannel request counts. It contains no
    /// private key material, and the caller may retain it.
    /// </returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<object?>(new
        {
            Options.Issuer,
            Options.Audience,
            CurrentKeyId,
            DiscoveryRequestCount,
            JwksRequestCount
        });

    /// <summary>Gets an OIDC discovery document suitable for JSON serialization.</summary>
    /// <returns>
    /// A newly allocated object containing the configured issuer, JWKS URI, and RSA-SHA256 signing
    /// algorithm. The caller may retain the returned object.
    /// </returns>
    public object GetDiscoveryDocument() => new
    {
        issuer = Options.Issuer,
        jwks_uri = $"{Options.Issuer.TrimEnd('/')}{NormalizePath(Options.JwksPath)}",
        id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 }
    };

    /// <summary>Gets the authority's public signing key as a JWKS document.</summary>
    /// <returns>
    /// A newly allocated JWKS-shaped object containing snapshots of every currently published
    /// public RSA key. It contains no private key material, and the caller may retain it.
    /// </returns>
    public object GetJsonWebKeySet()
    {
        lock (_keysLock)
        {
            var keys = _keys
                .Where(key => key.Published)
                .Select(key =>
                {
                    var parameters = key.Rsa.ExportParameters(includePrivateParameters: false);
                    return new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = key.SecurityKey.KeyId,
                        alg = SecurityAlgorithms.RsaSha256,
                        n = WebEncoders.Base64UrlEncode(parameters.Modulus!),
                        e = WebEncoders.Base64UrlEncode(parameters.Exponent!)
                    };
                })
                .ToArray();
            return new
            {
                keys
            };
        }
    }

    internal string WriteToken(
        string issuer,
        IEnumerable<string> audiences,
        IEnumerable<System.Security.Claims.Claim> claims,
        DateTimeOffset notBefore,
        DateTimeOffset expires)
    {
        SigningCredentials signingCredentials;
        lock (_keysLock)
        {
            signingCredentials = new SigningCredentials(
                _currentKey.SecurityKey,
                SecurityAlgorithms.RsaSha256);
        }

        return WriteToken(
            issuer,
            audiences,
            claims,
            notBefore,
            expires,
            signingCredentials);
    }

    internal string WriteUnsignedToken(
        string issuer,
        IEnumerable<string> audiences,
        IEnumerable<System.Security.Claims.Claim> claims,
        DateTimeOffset notBefore,
        DateTimeOffset expires) =>
        WriteToken(issuer, audiences, claims, notBefore, expires, signingCredentials: null);

    internal void Configure(
        JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        options.MetadataAddress =
            $"{Options.Issuer.TrimEnd('/')}{NormalizePath(Options.DiscoveryPath)}";
        options.BackchannelHttpHandler = new TestJwtBackchannelHandler(this);
        options.RefreshOnIssuerKeyNotFound = true;
        options.SaveToken = Options.SaveToken;
        var validation = options.TokenValidationParameters ?? new TokenValidationParameters();
        validation.ValidateIssuerSigningKey = true;
        validation.IssuerSigningKey = null;
        validation.IssuerSigningKeys = null;
        validation.ValidateIssuer = true;
        validation.ValidIssuer = Options.Issuer;
        validation.ValidateAudience = true;
        validation.ValidAudience = Options.Audience;
        validation.ValidateLifetime = true;
        validation.RequireExpirationTime = true;
        validation.RequireSignedTokens = true;
        validation.ClockSkew = Options.ClockSkew;
        validation.NameClaimType = "name";
        validation.RoleClaimType = AzureAdClaimTypes.Roles;
        options.TokenValidationParameters = validation;
    }

    private string CreateUntrustedToken(
        bool useKnownKeyId,
        Action<TestJwtBuilder>? configure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa)
        {
            KeyId = useKnownKeyId ? CurrentKeyId : Guid.NewGuid().ToString("N")
        };
        var builder = new TestJwtBuilder(this);
        configure?.Invoke(builder);
        return builder.Build(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
    }

    internal void RecordBackchannelRequest(string? path)
    {
        if (string.Equals(
            path,
            NormalizePath(Options.DiscoveryPath),
            StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _discoveryRequestCount);
        }
        else if (string.Equals(
            path,
            NormalizePath(Options.JwksPath),
            StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _jwksRequestCount);
        }
    }

    internal static string WriteToken(
        string issuer,
        IEnumerable<string> audiences,
        IEnumerable<System.Security.Claims.Claim> claims,
        DateTimeOffset notBefore,
        DateTimeOffset expires,
        SigningCredentials? signingCredentials)
    {
        var audienceValues = audiences.ToArray();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audienceValues.First(),
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            NotBefore = notBefore.UtcDateTime,
            Expires = expires.UtcDateTime,
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = signingCredentials
        };
        var token = new JwtSecurityTokenHandler().CreateJwtSecurityToken(descriptor);
        if (audienceValues.Length > 1)
        {
            token.Payload[JwtRegisteredClaimNames.Aud] = audienceValues;
        }

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Releases every RSA key owned by this authority.</summary>
    /// <remarks>Calling this method more than once has no effect.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_keysLock)
        {
            foreach (var key in _keys)
            {
                key.Rsa.Dispose();
            }

            _keys.Clear();
        }
    }

    internal static string NormalizePath(string path) =>
        path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";

    private static SigningKey CreateSigningKey(bool published)
    {
        var rsa = RSA.Create(2048);
        return new SigningKey(
            rsa,
            new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") },
            published);
    }

    private sealed class SigningKey(RSA rsa, RsaSecurityKey securityKey, bool published)
    {
        public RSA Rsa { get; } = rsa;

        public RsaSecurityKey SecurityKey { get; } = securityKey;

        public bool Published { get; set; } = published;
    }
}
