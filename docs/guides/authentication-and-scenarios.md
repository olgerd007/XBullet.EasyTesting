# Authentication and scenarios

This guide covers authenticated ASP.NET Core test hosts, composable scenarios, per-test isolation, and simulated or end-to-end authentication. It was separated from the project landing page so each workflow can be expanded without making the root README difficult to navigate.

## Controller authentication

Reference `XBullet.EasyTesting`, make the API entry point visible to the test project, and create a factory:

```csharp
public partial class Program;

public sealed class OrdersControllerTests
    : IClassFixture<AuthenticatedWebApplicationFactory<Program>>
{
    private readonly AuthenticatedWebApplicationFactory<Program> _factory;

    public OrdersControllerTests(AuthenticatedWebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Admin_can_get_orders()
    {
        using var client = _factory.Client()
            .AsUser(user => user
                .WithName("Ada")
                .WithNameIdentifier("user-42")
                .WithRole("Administrator")
                .WithClaim("permission", "orders.read"))
            .WithoutRedirects()
            .Build();
        var response = await client.GetAsync("/api/orders");

        response.EnsureSuccessStatusCode();
    }
}
```

The fluent client is anonymous until `AsUser` is called. Use `factory.Client().AsAnonymous().Build()` or `CreateAnonymousClient()` for `401 Unauthorized` scenarios. A user with insufficient roles or claims receives `403 Forbidden`. `AuthenticateAs` can also be applied to an individual `HttpRequestMessage`, so identities can vary while sharing one client and test server. Existing non-fluent APIs remain available.

For composable configuration without a custom factory subclass, build the host fluently. Multiple callbacks of each kind are applied in registration order, allowing extension packages and test-specific setup to participate independently:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .ConfigureConfiguration(configuration =>
        configuration.AddInMemoryCollection(testSettings))
    .ConfigureServices(services =>
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    })
    .ConfigureAuthentication(authentication => authentication
        .MapAzureAd("Bearer")
        .MapApiKey("ApiKey"))
    .Build();
```

Compose one arrange-and-request flow with `Scenario()`. The result owns both its client and response, so dispose it after assertions:

```csharp
using var result = await factory.Scenario()
    .Arrange(cancellationToken => SeedOrdersAsync(cancellationToken))
    .AsAzureAdUser(user => user
        .WithTenantId("tenant-42")
        .WithScope("orders.read"))
    .WithHeader("X-Correlation-Id", "test-42")
    .Get("/api/orders")
    .ExecuteAsync(cancellationToken);

await result.Should()
    .HaveStatusCode(HttpStatusCode.OK)
    .HaveJsonBodyAsync(
        new OrderResponse(42, "Ready"),
        cancellationToken: cancellationToken);
```

Response assertions are test-framework agnostic. In addition to status and structural JSON body
checks, a scenario result can assert successful responses and response or content headers.

## Per-test isolation

Use `TestScenarioScope` when a factory is shared by multiple tests. The factory holds its scenario gate for the complete test lifetime, creates a child host for reversible configuration and service replacements, resets registered mutable resources before and after the test, and invokes provider-specific database isolation hooks.

Register reusable HTTP stubs and message recorders once in the factory. Both implement `ITestScenarioResource`, so their rules, requests, and messages are reset automatically and captured if the test fails:

```csharp
public sealed class TestApiFactory
    : EntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
    public StubHttpMessageHandler ExternalCatalog { get; } = new();
    public RecordedMessageBus PublishedMessages { get; } = new();

    public TestApiFactory()
    {
        RegisterScenarioResource("External catalog", ExternalCatalog);
        RegisterScenarioResource("Published messages", PublishedMessages);
    }

    protected override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        TestScenarioContext context)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context.OnCleanup(connection.DisposeAsync);
        services.AddDbContext<TestApiDbContext>(options => options.UseSqlite(connection));
    }
}
```

If a test does not depend on relational behavior, the EF Core in-memory provider can be selected
without any database registration boilerplate:

```csharp
public sealed class TestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
```

Each scenario gets its own in-memory database. Because this provider does not enforce relational
constraints or support transactions, prefer SQLite or the production relational provider for tests
that exercise those behaviors.

Run the test body through `RunInTestScenarioScopeAsync` to guarantee failure diagnostics are captured before cleanup. The original exception is preserved, and `TestScenarioDiagnostics` is attached through `exception.Data[TestScenarioDiagnostics.ExceptionDataKey]`:

```csharp
await factory.RunInTestScenarioScopeAsync(
    async (scope, cancellationToken) =>
    {
        factory.ExternalCatalog
            .When(HttpMethod.Get, "/products/701")
            .RespondJson(externalProduct);

        using var client = scope.Client()
            .AsUser(TestUser.Create("scenario-user"))
            .Build();
        using var response = await client.PostAsync(
            "/api/products/import/701",
            content: null,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    },
    configure: scope => scope
        .ConfigureConfiguration(configuration =>
            configuration.AddInMemoryCollection(testSettings))
        .ConfigureServices(services =>
            services.AddSingleton<IClock>(fakeClock)),
    cancellationToken);
```

For manual lifetime control, use `await using var scope = await factory.CreateTestScenarioScopeAsync(...)`. Override `ConfigureScenarioDatabaseServices` to create a database or schema named from `context.ScenarioId`; scenario-owned connections and containers can be registered with `DisposeWithScenario` or `OnCleanup`.

External dependencies that must start before the application reads its configuration can implement
`ITestScenarioEnvironmentResource`. Configure a fresh resource for each scenario; after startup it
can contribute dynamic configuration and service registrations, and the scope makes the typed
resource available to test code:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .ConfigureEnvironment(environment => environment
        .AddResource("database", context =>
            new CustomDatabaseResource(context.ScenarioId)))
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync(cancellationToken: cancellationToken);
var database = scope.GetEnvironmentResource<CustomDatabaseResource>("database");
```

The lifecycle is startup and readiness, configuration and service registration, host startup,
failure diagnostics, host shutdown, and reverse-order resource disposal. Derived factories can
override `ConfigureScenarioEnvironment`. The upcoming optional container packages build on this
lifecycle without adding container dependencies to the core package.

For application-specific dependency replacement, derive from the factory and override `ConfigureServicesForTests`:

```csharp
public sealed class ApiFactory : AuthenticatedWebApplicationFactory<Program>
{
    protected override void ConfigureServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    }
}
```

Minimal-hosting applications sometimes read a connection string or another setting immediately
after `WebApplication.CreateBuilder`. Use early host settings for those values; ordinary
`ConfigureConfiguration` overrides remain appropriate for values consumed later:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UseSetting("ConnectionStrings:Orders", testDatabaseConnectionString)
    .ConfigureConfiguration(configuration =>
        configuration.AddInMemoryCollection(otherTestSettings))
    .Build();
```

Derived factories can override `ConfigureTestHostSettings`, and callers that do not use the fluent
builder can use `AuthenticatedWebApplicationFactory<Program>.CreateWithHostSettings(...)`. These
APIs apply the settings early enough for top-level minimal-hosting startup code.

### Startup-based hosts

Applications that already expose an `IntegrationTestStartup` can keep that host and avoid invoking
`Program.Main`. Derive from `StartupAuthenticatedWebApplicationFactory<TStartup>`; it builds the
startup pipeline directly on `TestServer`, uses the startup assembly output as its content root, and
retains all configuration, authentication, and scenario hooks:

```csharp
public sealed class StartupTestHost
    : StartupAuthenticatedWebApplicationFactory<IntegrationTestStartup>
{
    protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://identity.example.test"
        });

    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication) =>
        authentication.MapFederation("Federation");
}
```

Use `StartupEntityFrameworkWebApplicationFactory<TStartup, TDbContext>` when the same host also
needs the framework database helpers and per-scenario database isolation.

The Federation profile creates a primary identity whose authentication type defaults to
`Federation` and an additional identity whose authentication type defaults to `ApiUserIdentity`:

```csharp
using var client = scope.Client()
    .AsFederatedUser(user => user
        .WithNameIdentifier("user-42")
        .WithFederationClaim("tenant", "tenant-42")
        .WithApiUserClaim("portfolio", "portfolio-17"))
    .Build();
```

If an application requires the additional identity to be a concrete `ApiUserIdentity` subclass, derive from
`TestClaimsPrincipalFactory`, override `CreateAdditionalIdentity`, and replace
`ITestClaimsPrincipalFactory` in `ConfigureServicesForTests` (or
`ConfigureAdditionalServicesForTests` on the EF host). The transported user profile remains
serializable while the application controls the server-side identity type.

The test authentication handler is installed only in the test host. It is never registered by the application itself.

### Multiple authentication schemes

Map test profiles to the scheme names used by the application. Mapped schemes replace their production handlers only inside the test server, so policies that explicitly select `Bearer`, `ApiKey`, or another scheme remain testable:

```csharp
public sealed class ApiFactory : AuthenticatedWebApplicationFactory<Program>
{
    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .MapAzureAd("Bearer")
            .MapApiKey("ApiKey")
            .MapFederation("Federation")
            .MapScheme("PartnerScheme");
    }
}
```

Create scheme-specific clients fluently:

```csharp
using var azureAdClient = factory.Client()
    .AsAzureAdUser(user => user
        .WithObjectId("user-42")
        .WithTenantId("tenant-42")
        .WithPreferredUsername("ada@example.test")
        .WithScopes("orders.read", "profile")
        .WithAppRole("Administrator"))
    .Build();

using var apiKeyClient = factory.Client()
    .AsApiKey(apiKey => apiKey
        .WithKeyId("partner-key")
        .WithClientName("Fulfilment partner")
        .WithClaim("region", "eu"))
    .Build();
```

Azure AD profiles expose common `oid`, `tid`, `preferred_username`, `scp`, `roles`, and `azp` claims. API-key profiles expose a non-secret `api_key_id`. Both support custom claims and roles. For another authentication type, map its scheme and use `TestUser.CreateBuilder().WithAuthenticationScheme(...)`.

These profiles test controller authentication and authorization without external identity infrastructure. They intentionally bypass signature, issuer, token-expiry, and secret validation.

### Hybrid simulated and real authentication

By default, XBullet keeps its existing behavior and selects simulated authentication as the test
host default. Opt into coexistence when the application must keep its real default handler:

```csharp
protected override void ConfigureTestAuthentication(
    TestAuthenticationSchemeBuilder authentication) =>
    authentication
        .PreserveDefaultAuthenticationScheme()
        .MapTestAuthentication("IntegrationTest");

using var client = factory.Client()
    .AsUser(
        user => user.WithName("Ada").WithRole("Administrator"),
        authenticationScheme: "IntegrationTest")
    .Build();
```

Policies intended for simulated identities should name `IntegrationTest`; the application's
default policy continues to use its real scheme. This lets one host and test project use both
approaches:

When real and simulated identities must authorize against the same endpoint using plain
`RequireAuthorization()`, opt into the hybrid default instead:

```csharp
protected override void ConfigureTestAuthentication(
    TestAuthenticationSchemeBuilder authentication) =>
    authentication.UseHybridDefaultAuthentication("IntegrationTest");
```

The hybrid policy scheme selects `IntegrationTest` only when the XBullet test-identity header is
present. All other requests use the application's original default authentication scheme, while
challenge and forbid operations continue to use the application's original handlers. The existing
preserved-default behavior remains unchanged for hosts that do not opt in.

- Simulated identities exercise authorization policies and business behavior quickly.
- Real handlers exercise registration, login, password changes, token validation, refresh, and
  other credential-lifecycle behavior.
- ASP.NET Core Identity users can be persisted through `SeedIdentityUserAsync`, then converted to a
  linked simulated user through `CreateIdentityTestUserAsync` or `UserManager.CreateTestUserAsync`.
  Stores without role or claim support produce linked users with empty role or claim collections.
- A token returned by an Identity API login endpoint can be sent with
  `factory.Client().WithBearerToken(accessToken)`; no JWT authority or JWT-specific test
  configuration is required because the application's own Identity bearer handler validates it.

Keep registration and credential-lifecycle tests on the real handler. Use the linked simulated
principal for downstream authorization tests where cryptographic token validation is not the
behavior under test.

### End-to-end authentication

For validation through the application's real handlers, keep `AddJwtBearer` and the application's API-key handler registered in production code, then opt the test factory into end-to-end mode:

```csharp
protected override void ConfigureTestAuthentication(
    TestAuthenticationSchemeBuilder authentication)
{
    authentication
        .UseEndToEndJwt("Bearer", authority => authority
            .WithIssuer("https://identity.orders.test")
            .WithAudience("orders-api")
            .WithTokenLifetime(TimeSpan.FromMinutes(2))
            .WithClockSkew(TimeSpan.Zero)
            .SaveAccessToken())
        .UseEndToEndApiKey("ApiKey", apiKey => apiKey
            .WithHeaderName("X-Api-Key")
            .WithQueryParameterName("api_key"))
        .UseEndToEndClientCertificate("Certificate");
}
```

`UseEndToEndJwt` connects the named `JwtBearerHandler` to an in-memory OIDC backchannel. The real handler loads discovery metadata and signing keys, validates the token, and refreshes JWKS after key rotation. The same OIDC discovery and JWKS documents are exposed from the test host at `/.well-known/openid-configuration` and `/.well-known/jwks.json`. Tokens can customize issuer, audience, expiry, scopes, roles, subject, name, and arbitrary claims:

```csharp
using var client = scope.Client()
    .AsJwt(token => token
        .WithSubject("user-42")
        .WithName("Ada")
        .WithAudience("orders-api")
        .WithScope("orders.read")
        .WithRole("Administrator")
        .ExpiresAfter(TimeSpan.FromMinutes(1)))
    .Build();
```

Negative scenarios are explicit: `AsExpiredJwt()`, `AsMalformedJwt()`, `AsJwtWithWrongAudience()`, `AsJwtWithWrongIssuer()`, `AsJwtWithInvalidSignature()`, `AsJwtWithUnknownKey()`, `AsUnsignedJwt()`, and `AsJwtNotYetValid()`. Real API keys can be injected with `WithApiKeyHeader(value)` or `WithApiKeyQuery(value)`; these helpers only transport the credential, leaving parsing and validation to the application's registered API-key handler.

Register multiple authorities with unique discovery and JWKS paths, then select one by scheme. Authorities are also available from the scenario scope for explicit rotation:

```csharp
authentication.UseEndToEndJwt("PartnerBearer", authority => authority
    .WithIssuer("https://partner.orders.test")
    .WithAudience("orders-partner-api")
    .WithDiscoveryPath("/.well-known/partner/openid-configuration")
    .WithJwksPath("/.well-known/partner/jwks.json")
    .WithoutDefaultScheme());

using var partnerClient = scope.Client()
    .AsJwt("PartnerBearer", token => token.WithClaim("partner", "trusted"))
    .Build();

scope.JwtAuthority("PartnerBearer").RotateSigningKey();
```

Authentication events are reset with each `TestScenarioScope`, included in failure diagnostics, and contain no headers, query strings, or raw credentials. Assertions can be chained after a request:

```csharp
scope.AuthenticationEvents.Should()
    .HaveValidationFailure(authenticationScheme: "Bearer")
    .HaveChallenge(authenticationScheme: "Bearer")
    .NotHave(TestAuthenticationEventKind.Forbidden);
```

For mTLS, leave the application's real `AddCertificate` scheme registered and use `WithClientCertificate(...)`. XBullet creates a self-signed client certificate, transports it through TestServer as an HTTPS connection certificate, removes the internal transport header, and lets `CertificateAuthenticationHandler` perform validation:

```csharp
using var client = scope.Client()
    .WithClientCertificate(certificate => certificate
        .WithSubject("CN=trusted-test-client"))
    .Build();
```

Calling `SaveAccessToken()` on the authority sets the real bearer handler's `SaveToken` option, making the access token available through `AuthenticateAsync().Properties`. A standalone authority can be created with `using var authority = TestJwtAuthority.Create(...)` when a token is needed outside the HTTP client builder.

Simulated users can represent composite principals and authentication-ticket state when handler validation is not under test:

```csharp
using var client = scope.Client()
    .AsUser(user => user
        .WithName("Primary identity")
        .WithIdentity(identity => identity
            .WithAuthenticationType("DelegatedIdentity")
            .WithClaim(ClaimTypes.Name, "Secondary identity"))
        .WithAuthenticationProperty("refresh_token", "test-value"))
    .Build();
```

Anonymous controllers continue to work normally. When the application uses a fallback policy that requires authentication, mark public controllers such as health endpoints with `[AllowAnonymous]` and test them with `CreateAnonymousClient()`.

The sample API also demonstrates a database-backed controller. `TestApiFactory` derives from `EntityFrameworkWebApplicationFactory<Program, TestApiDbContext>` and replaces its file-based EF Core SQLite registration with an open in-memory SQLite connection. This exercises controller routing, authorization, EF Core queries, mapping, and serialization without sharing persistent data between test fixtures.

The EF Core factory provides generic operations:

```csharp
await factory.InitializeDatabaseAsync();
await factory.RecreateDatabaseAsync();
await factory.SeedDatabaseAsync([new Product { Id = 42, Name = "Keyboard" }]);

var count = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products.CountAsync(cancellationToken));

await factory.ExecuteDatabaseAsync((database, cancellationToken) =>
{
    database.Products.Add(new Product { Id = 43, Name = "Mouse" });
    return Task.CompletedTask;
});

await factory.ExecuteInTransactionAsync(async (database, cancellationToken) =>
{
    // Changes are saved and committed when the callback succeeds.
});
```

Application registrations made with `AddDbContextFactory<TContext>` are supported. When the test
factory replaces a database, both `TContext` and `IDbContextFactory<TContext>` registrations are
removed before the test provider is added.

Override the scenario database lifecycle when `EnsureDeleted`/`EnsureCreated` does not match the
application's schema process:

```csharp
protected override Task InitializeScenarioDatabaseAsync(
    AppDbContext database,
    CancellationToken cancellationToken) =>
    applicationDatabaseInitializer.InitializeAsync(database, cancellationToken);

protected override Task CleanupScenarioDatabaseAsync(
    AppDbContext database,
    CancellationToken cancellationToken) =>
    applicationDatabaseInitializer.CleanupAsync(database, cancellationToken);
```

For one fluent arrangement, use `Database().RecreateDatabaseWith(...)`. The defaults remain
backward-compatible. Default SQLite scenario cleanup clears connection pools before deletion,
retries transient file-lock failures, and attaches `SqliteDatabaseCleanupDiagnostics` to the final
exception under `SqliteDatabaseCleanupDiagnostics.ExceptionDataKey`.

Related setup can also be composed and saved once as a fluent database scenario:

```csharp
await factory.Database()
    .Recreate()
    .Seed(new Product { Id = 42, Name = "Keyboard" })
    .Apply(database =>
    {
        database.Products.Local.Single(product => product.Id == 42).Name = "Keyboard Pro";
    })
    .InTransaction()
    .ExecuteAsync(cancellationToken);
```

Each action resolves a fresh scoped `DbContext`. Operations invoked through the factory are serialized to avoid overlapping reset/seed operations. `RecreateDatabaseAsync` deletes the entire configured database, so the factory must point to an isolated test database.

### CRUD controller example

The sample `ProductsController` is an authenticated, database-backed CRUD resource:

| Method | Route | Result |
| --- | --- | --- |
| `GET` | `/api/products` | Ordered product collection |
| `GET` | `/api/products/{id}` | Product or `404 Not Found` |
| `POST` | `/api/products` | Persisted product and `201 Created` with `Location` |
| `PUT` | `/api/products/{id}` | Updated product or `404 Not Found` |
| `DELETE` | `/api/products/{id}` | `204 No Content` or `404 Not Found` |

Create and update requests validate the required 200-character name and positive price through `[ApiController]`. The integration tests recreate or seed the database, send real JSON requests through TestServer, and query a fresh EF Core context to confirm the final stored state.
