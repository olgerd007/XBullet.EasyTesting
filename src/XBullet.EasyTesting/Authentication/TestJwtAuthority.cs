using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Creates locally signed JWTs and publishes their OIDC/JWKS metadata in a test host.</summary>
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
    public TestJwtAuthorityOptions Options { get; }

    /// <summary>Gets the identifier of the key used to sign new tokens.</summary>
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
    public int DiscoveryRequestCount => Volatile.Read(ref _discoveryRequestCount);

    /// <summary>Gets the number of JWKS requests made through the JWT handler backchannel.</summary>
    public int JwksRequestCount => Volatile.Read(ref _jwksRequestCount);

    /// <summary>Creates a standalone local JWT authority.</summary>
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
    public string CreateToken(Action<TestJwtBuilder>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var builder = new TestJwtBuilder(this);
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Creates a locally signed token that is already expired.</summary>
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
    public string CreateWrongAudienceToken(Action<TestJwtBuilder>? configure = null) =>
        CreateToken(builder =>
        {
            builder.WithAudience($"{Options.Audience}-wrong");
            configure?.Invoke(builder);
        });

    /// <summary>Creates a locally signed token with an invalid issuer.</summary>
    public string CreateWrongIssuerToken(Action<TestJwtBuilder>? configure = null) =>
        CreateToken(builder =>
        {
            builder.WithIssuer($"{Options.Issuer.TrimEnd('/')}/wrong");
            configure?.Invoke(builder);
        });

    /// <summary>Creates a token whose signature does not match its known key identifier.</summary>
    public string CreateInvalidSignatureToken(Action<TestJwtBuilder>? configure = null) =>
        CreateUntrustedToken(useKnownKeyId: true, configure);

    /// <summary>Creates a signed token with a key identifier absent from JWKS.</summary>
    public string CreateUnknownKeyToken(Action<TestJwtBuilder>? configure = null) =>
        CreateUntrustedToken(useKnownKeyId: false, configure);

    /// <summary>Creates an unsigned token.</summary>
    public string CreateUnsignedToken(Action<TestJwtBuilder>? configure = null)
    {
        var builder = new TestJwtBuilder(this);
        configure?.Invoke(builder);
        return builder.BuildUnsigned();
    }

    /// <summary>Creates a validly signed token that cannot be used until a future time.</summary>
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
    public object GetDiscoveryDocument() => new
    {
        issuer = Options.Issuer,
        jwks_uri = $"{Options.Issuer.TrimEnd('/')}{NormalizePath(Options.JwksPath)}",
        id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 }
    };

    /// <summary>Gets the authority's public signing key as a JWKS document.</summary>
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

    /// <inheritdoc />
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
