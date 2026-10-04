# Understand authentication models

XBullet.EasyTesting supports two distinct authentication layers. Simulated authentication creates
a principal inside the test host. End-to-end authentication sends a credential through the
application's real ASP.NET Core authentication handler. Choose the layer that matches the behavior
the test must prove.

## Simulated versus end-to-end authentication

| Model | Proves | Does not prove |
| --- | --- | --- |
| Simulated user | Authorization policies, roles, claims, application behavior, and scheme selection | Credential parsing, signature validation, expiry validation, or production challenge behavior |
| End-to-end JWT | Bearer handler configuration, token validation, discovery/JWKS, authentication properties, and authorization | Behavior of an external identity provider outside the local test authority |
| End-to-end API key | The application's real API-key parser and validator | Security of a production key store |
| End-to-end certificate | The application's certificate handler and authorization | External TLS termination or infrastructure certificate forwarding |

Use simulated identities for most business and authorization tests. Add focused end-to-end tests
for every production credential handler.

## Build a simulated user

`AsUser` creates a general `TestUser`. Add the name, subject identifier, roles, claims, additional
identities, and authentication properties required by the test. Omitted data is not invented by the
library.

An anonymous request to an authenticated endpoint normally receives `401 Unauthorized`. An
authenticated user that does not satisfy a role or claim requirement normally receives
`403 Forbidden`. Use `WithoutRedirects` when cookie handlers might otherwise turn those responses
into redirects.

## Map named schemes

Policies that name `Bearer`, `ApiKey`, or another scheme require the test host to map that exact
scheme. Configure mappings once in a factory or host builder:

```csharp
protected override void ConfigureTestAuthentication(
    TestAuthenticationSchemeBuilder authentication) =>
    authentication
        .MapAzureAd("AzureAd")
        .MapApiKey("ApiKey");
```

Then select the matching profile for the request:

<!-- snippet: tests/TestApi.IntegrationTests/AuthenticationScenarioTests.cs#docs-simulated-azure-ad-user -->
```csharp
[Fact]
public Task Azure_ad_profile_can_access_its_scheme_and_policy() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = scope.Client()
            .AsAzureAdUser(user => user
                .WithObjectId("user-object-42")
                .WithTenantId("tenant-42")
                .WithPreferredUsername("ada@example.test")
                .WithScope("orders.read"))
            .Build();

        using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);
        using var genericResponse = await client.GetAsync("/api/secure/me", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, genericResponse.StatusCode);
    });
```
<!-- end-snippet -->

The specialized Azure AD builder provides common `oid`, `tid`, `preferred_username`, `scp`, and
`roles` claims. The API-key builder provides a simulated key identity. These profiles still create
principals inside the test server; they do not parse a bearer token or API-key value.

## Preserve or combine default schemes

By default, test authentication can replace the host's default scheme. Use
`PreserveDefaultAuthenticationScheme().MapTestAuthentication("IntegrationTest")` when real
application handlers must remain the default and individual tests explicitly select the simulated
scheme.

Use `UseHybridDefaultAuthentication("IntegrationTest")` when the same plain
`RequireAuthorization()` endpoint must accept both real credentials and simulated principals. A
request carrying the XBullet test-identity header uses the simulated scheme; other requests use the
application's original default. Hybrid behavior is opt-in so a simulated header cannot silently
change production-handler tests.

With ASP.NET Core Identity API endpoints, enable hybrid authentication only in the test factory.
Send an access token returned by `/login?useCookies=false` with `WithBearerToken(token)`, or choose
`AsUser(...)` for a simulated identity. Both modes can exercise the same ordinary `[Authorize]`
endpoint without changing its production policies. Identity access tokens use the real Identity
bearer handler; they do not require `UseEndToEndJwt`.

If both a bearer token and a simulated header are present, the simulated header selects the
authentication mode. An invalid simulated payload fails authentication without falling back to the
bearer token. Challenges and forbidden responses retain the application's original schemes.

Hybrid selection applies to default authentication. An endpoint or policy that explicitly names
`Identity.Bearer` continues to require that scheme and does not accept a simulated identity through
the hybrid default. Executable Identity login, routing, and authorization coverage lives in
[`IdentityHybridAuthenticationTests`](../../tests/TestApi.IntegrationTests/IdentityHybridAuthenticationTests.cs).

## Use multiple identities

`WithIdentity` adds another identity to the principal. `MapFederation` and `AsFederatedUser` build a
primary federation identity plus an `ApiUserIdentity`-shaped identity. Replace
`ITestClaimsPrincipalFactory` with a derived `TestClaimsPrincipalFactory` when an application
requires a concrete identity subclass.

Authentication properties can be supplied with `WithAuthenticationProperty`. Keep values
synthetic: diagnostics should not contain real refresh tokens, bearer tokens, API keys, or personal
data.

## Configure end-to-end handlers

Map the application's real schemes to local test credential sources in the test factory:

<!-- snippet: tests/TestApi.IntegrationTests/EndToEndAuthenticationFactory.cs#docs-end-to-end-authentication-factory -->
```csharp
public sealed class EndToEndAuthenticationFactory : TestApiFactory
{
    protected override void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .UseEndToEndJwt("AzureAd", authority => authority
                .WithIssuer("https://identity.xbullet.test")
                .WithAudience("test-api")
                .WithTokenLifetime(TimeSpan.FromMinutes(2))
                .WithClockSkew(TimeSpan.Zero)
                .SaveAccessToken())
            .UseEndToEndJwt("PartnerBearer", authority => authority
                .WithIssuer("https://partner.xbullet.test")
                .WithAudience("test-api-partner")
                .WithDiscoveryPath("/.well-known/partner/openid-configuration")
                .WithJwksPath("/.well-known/partner/jwks.json")
                .WithoutDefaultScheme())
            .UseEndToEndApiKey("ApiKey", apiKey => apiKey
                .WithHeaderName("X-Api-Key")
                .WithQueryParameterName("api_key"))
            .UseEndToEndClientCertificate("Certificate");
    }
}
```
<!-- end-snippet -->

`UseEndToEndJwt` exposes local OpenID Connect discovery and JWKS endpoints, issues signed tokens,
and routes the real bearer handler's backchannel to the test authority. Configure issuer, audience,
lifetime, clock skew, paths, and whether the scheme becomes the default.

The request then travels through the real handler:

<!-- snippet: tests/TestApi.IntegrationTests/EndToEndAuthenticationTests.cs#docs-end-to-end-jwt -->
```csharp
[Fact]
public Task Locally_signed_jwt_is_validated_by_the_real_bearer_handler() =>
    Run(async (scope, cancellationToken) =>
    {
        using var result = await scope.Scenario()
            .AsJwt(token => token
                .WithSubject("user-42")
                .WithName("Ada")
                .WithClaim("tid", "tenant-42")
                .WithScope("orders.read")
                .ExpiresAfter(TimeSpan.FromMinutes(1)))
            .Get("/api/secure/azure-ad")
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
    });
```
<!-- end-snippet -->

Negative helpers cover expired, malformed, wrong-audience, wrong-issuer, invalid-signature,
unknown-key, unsigned, and not-yet-valid JWTs. Authentication events can assert validation failure,
challenge, success, and forbid behavior by scheme.

`WithApiKeyHeader` and `WithApiKeyQuery` only transport a real key. The application's registered
handler remains responsible for parsing and validation. Query-key diagnostics must redact the
credential. Certificate helpers similarly exercise the registered certificate scheme but do not
model an external TLS proxy.

## Executable coverage

The repository keeps each major authentication path executable:

- [`AuthenticatedControllerTests`](../../tests/TestApi.IntegrationTests/AuthenticatedControllerTests.cs)
  covers anonymous requests, general users, roles, and custom claims.
- [`AuthenticationScenarioTests`](../../tests/TestApi.IntegrationTests/AuthenticationScenarioTests.cs)
  covers mapped Azure AD and API-key schemes, forbidden claims, multiple identities, and
  authentication properties.
- [`StartupHostTests`](../../tests/TestApi.IntegrationTests/StartupHostTests.cs) covers federation
  identities and a custom claims-principal factory.
- [`EndToEndAuthenticationTests`](../../tests/TestApi.IntegrationTests/EndToEndAuthenticationTests.cs)
  covers valid and invalid JWTs, roles, JWKS rotation, authentication events, saved tokens, client
  certificates, discovery endpoints, and real API-key injection.

## Security and cleanup

- Never use production signing keys, API keys, certificates, access tokens, or personal data.
- Authentication diagnostics intentionally omit bearer-token contents and sensitive URI values.
- Dispose clients and results so headers and authentication state do not leak across requests.
- Use a scenario scope when authentication event recorders or other mutable resources are shared by
  a fixture.

## Related documentation

- [Run your first controller test](../getting-started/first-controller-test.md)
- [Choose a test host](test-hosts.md)
- [Scenarios and isolation](scenarios-and-isolation.md)
