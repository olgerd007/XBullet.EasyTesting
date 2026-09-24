using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Diagnostics;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AuthenticationCoverageTests
{
    [Fact]
    public void User_and_specialized_builders_validate_optional_and_collection_inputs()
    {
        var user = TestUser.CreateBuilder()
            .WithAuthenticationScheme("Custom")
            .WithAuthenticationType("CustomType")
            .WithNameIdentifier("user-42")
            .WithName("Ada")
            .WithRoles("admin", "operator")
            .WithClaim("tenant", "tenant-42")
            .WithIdentity(identity => identity
                .WithAuthenticationType("Secondary")
                .WithClaim("source", "delegated"))
            .WithAuthenticationProperty("refresh_token", null)
            .Build();
        Assert.Equal(2, user.Roles.Count);
        Assert.Single(user.AdditionalIdentities);
        Assert.Contains(user.ToClaims(), claim => claim.Value == "admin");
        Assert.Contains(user.ToClaims(), claim => claim.Value == "tenant-42");
        Assert.Equal(
            "delegated",
            Assert.Single(user.AdditionalIdentities).ToClaimsIdentity().FindFirst("source")?.Value);

        var userBuilder = TestUser.CreateBuilder();
        Assert.Throws<ArgumentException>(() => userBuilder.WithAuthenticationScheme(" "));
        Assert.Throws<ArgumentException>(() => userBuilder.WithAuthenticationType(" "));
        Assert.Throws<ArgumentException>(() => userBuilder.WithNameIdentifier(" "));
        Assert.Throws<ArgumentException>(() => userBuilder.WithName(" "));
        Assert.Throws<ArgumentNullException>(() => userBuilder.WithRoles(null!));
        Assert.Throws<ArgumentException>(() => userBuilder.WithRole(" "));
        Assert.Throws<ArgumentException>(() => userBuilder.WithClaim(" ", "value"));
        Assert.Throws<ArgumentNullException>(() => userBuilder.WithClaim("type", null!));
        Assert.Throws<ArgumentNullException>(() => userBuilder.WithIdentity(null!));
        Assert.Throws<ArgumentException>(() => userBuilder.WithAuthenticationProperty(" ", null));

        var apiKey = new TestApiKeyBuilder()
            .WithKeyId("key-42")
            .WithClientName("client")
            .WithRoles("reader", "writer")
            .WithClaim("region", "eu")
            .Build();
        Assert.Contains("reader", apiKey.Roles);
        var apiKeyBuilder = new TestApiKeyBuilder();
        Assert.Throws<ArgumentException>(() => apiKeyBuilder.WithKeyId(" "));
        Assert.Throws<ArgumentException>(() => apiKeyBuilder.WithClientName(" "));
        Assert.Throws<ArgumentNullException>(() => apiKeyBuilder.WithRoles(null!));
        Assert.Throws<ArgumentException>(() => apiKeyBuilder.WithRole(" "));

        var azureAd = new TestAzureAdUserBuilder()
            .WithObjectId("object")
            .WithTenantId("tenant")
            .WithClientId("client")
            .WithPreferredUsername("ada@example.test")
            .WithName("Ada")
            .WithScopes("read", "write")
            .WithAppRoles("admin")
            .WithClaim("custom", "value")
            .Build();
        Assert.Contains(azureAd.Claims, claim => claim.Type == AzureAdClaimTypes.Scope);
        var azureBuilder = new TestAzureAdUserBuilder();
        Assert.Throws<ArgumentNullException>(() => azureBuilder.WithScopes(null!));
        Assert.Throws<ArgumentNullException>(() => azureBuilder.WithAppRoles(null!));
        Assert.Throws<ArgumentException>(() => azureBuilder.WithScope(" "));
        Assert.Throws<ArgumentException>(() => azureBuilder.WithAppRole(" "));
    }

    [Fact]
    public async Task Jwt_authority_and_authentication_assertions_cover_optional_failure_paths()
    {
        using var authority = TestJwtAuthority.Create();
        var token = authority.CreateToken(jwt => jwt
            .WithIssuer(authority.Options.Issuer)
            .WithAudience(authority.Options.Audience)
            .AddAudience("secondary")
            .WithSubject("subject")
            .WithName("Ada")
            .WithScopes("read", "write")
            .WithRoles("admin")
            .WithClaim("custom", "value")
            .NotBefore(DateTimeOffset.UtcNow.AddMinutes(-1))
            .ExpiresAfter(TimeSpan.FromMinutes(5)));
        Assert.NotEmpty(token);
        Assert.NotEmpty(authority.CreateToken());
        Assert.NotEmpty(authority.CreateUnsignedToken());
        Assert.NotEmpty(authority.CreateExpiredToken());
        Assert.NotEmpty(authority.CreateFutureNotBeforeToken());
        Assert.NotEmpty(authority.CreateWrongAudienceToken());
        Assert.NotEmpty(authority.CreateWrongIssuerToken());
        Assert.NotEmpty(authority.CreateUnknownKeyToken());
        Assert.NotEmpty(authority.CreateInvalidSignatureToken());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            authority.CreateToken(jwt => jwt.ExpiresAfter(TimeSpan.Zero)));
        Assert.Throws<ArgumentNullException>(() =>
            authority.CreateToken(jwt => jwt.WithScopes(null!)));
        Assert.Throws<ArgumentNullException>(() =>
            authority.CreateToken(jwt => jwt.WithRoles(null!)));
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.NotNull(await authority.CaptureDiagnosticsAsync(cancellationToken));
        await authority.ResetAsync(cancellationToken);

        var recorder = new TestAuthenticationEventRecorder();
        recorder.Should()
            .HaveChallenge(0)
            .HaveForbidden(0, "scheme")
            .HaveValidationFailure(0)
            .HaveValidatedCredential(0)
            .NotHave(TestAuthenticationEventKind.TokenValidated);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Should().HaveChallenge(-1));
        var failure = Assert.Throws<TestAuthenticationEventVerificationException>(() =>
            recorder.Should().HaveChallenge());
        Assert.Contains("No authentication events", failure.Message);
        Assert.NotNull(await recorder.CaptureDiagnosticsAsync(cancellationToken));
        await recorder.ResetAsync(cancellationToken);

        recorder.Record(
            TestAuthenticationEventKind.Challenge,
            "Bearer",
            "GET",
            "/secure");
        recorder.Record(
            TestAuthenticationEventKind.ValidationFailed,
            "Bearer",
            "POST",
            "/token",
            new InvalidOperationException("invalid token"));
        recorder.Should().HaveChallenge(authenticationScheme: "Bearer");
        var populatedFailure = Assert.Throws<TestAuthenticationEventVerificationException>(() =>
            recorder.Should().HaveForbidden(authenticationScheme: "Bearer"));
        Assert.Contains("Recorded events", populatedFailure.Message);
        Assert.Contains("InvalidOperationException", populatedFailure.Message);
    }

    [Fact]
    public void Authentication_scheme_builder_covers_registration_selection_and_validation()
    {
        var empty = new TestAuthenticationSchemeBuilder();
        Assert.Null(empty.DefaultEndToEndScheme);
        Assert.Throws<InvalidOperationException>(() => empty.GetJwtAuthority());
        Assert.Throws<InvalidOperationException>(() => empty.GetJwtAuthority("missing"));
        Assert.Throws<InvalidOperationException>(() => empty.GetClientCertificateRegistration());
        Assert.Throws<InvalidOperationException>(() =>
            empty.GetClientCertificateRegistration("missing"));

        var jwt = new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.UseAsDefaultScheme = true);
        Assert.Equal("Bearer", jwt.DefaultEndToEndScheme);
        Assert.Same(jwt.GetJwtAuthority(), jwt.GetJwtAuthority("Bearer"));
        Assert.Throws<InvalidOperationException>(() => jwt.GetJwtAuthority("missing"));
        Assert.Throws<InvalidOperationException>(() => jwt.UseEndToEndJwt("Bearer"));
        Assert.Throws<InvalidOperationException>(() => jwt.UseEndToEndJwt(
            "Other",
            options => options.DiscoveryPath = jwt.GetJwtAuthority().Options.DiscoveryPath));

        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.Issuer = " "));
        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.Audience = " "));
        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.DiscoveryPath = " "));
        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.JwksPath = " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.TokenLifetime = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndJwt("Bearer", options => options.ClockSkew = TimeSpan.FromSeconds(-1)));

        var apiKey = new TestAuthenticationSchemeBuilder()
            .UseEndToEndApiKey("ApiKey", options => options.UseAsDefaultScheme = true);
        Assert.Equal("ApiKey", apiKey.DefaultEndToEndScheme);
        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndApiKey("ApiKey", options => options.HeaderName = " "));
        Assert.Throws<ArgumentException>(() => new TestAuthenticationSchemeBuilder()
            .UseEndToEndApiKey("ApiKey", options => options.QueryParameterName = " "));

        var certificate = new TestAuthenticationSchemeBuilder()
            .UseEndToEndClientCertificate(
                "Certificate",
                options => options.UseAsDefaultScheme = true);
        Assert.Equal("Certificate", certificate.DefaultEndToEndScheme);
        Assert.Equal(
            "Certificate",
            certificate.GetClientCertificateRegistration().AuthenticationScheme);
        Assert.Equal(
            "Certificate",
            certificate.GetClientCertificateRegistration("Certificate").AuthenticationScheme);
        Assert.Throws<InvalidOperationException>(() =>
            certificate.GetClientCertificateRegistration("missing"));
        Assert.Throws<InvalidOperationException>(() =>
            certificate.UseEndToEndClientCertificate("Certificate"));
    }

    [Theory]
    [InlineData(null, "<no URI>")]
    [InlineData(" ", "<no URI>")]
    [InlineData("/products", "/products")]
    [InlineData("/products?name=book", "/products?name=book")]
    [InlineData("/products?token=secret", "/products?token={Redacted}")]
    [InlineData("/products?token", "/products?token")]
    [InlineData("/products?api%5Fkey=secret#details", "/products?api%5Fkey={Redacted}#details")]
    [InlineData("/products?%ZZ=secret", "/products?%ZZ=secret")]
    public void Uri_diagnostics_redact_sensitive_and_preserve_safe_values(
        string? value,
        string expected)
    {
        Assert.Equal(expected, UriDiagnosticFormatter.Format(value));
    }

    [Fact]
    public void Uri_diagnostics_support_absolute_and_null_uri_values()
    {
        Assert.Equal("<no URI>", UriDiagnosticFormatter.Format((Uri?)null));
        Assert.Equal(
            "/products?sig={Redacted}",
            UriDiagnosticFormatter.Format(new Uri("https://example.test/products?sig=secret")));
        Assert.True(UriDiagnosticFormatter.IsSensitiveQueryParameter("TOKEN"));
        Assert.False(UriDiagnosticFormatter.IsSensitiveQueryParameter("page"));
    }
}
