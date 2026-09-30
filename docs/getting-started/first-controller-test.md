# Run your first controller test

This tutorial creates an authenticated request through the in-memory ASP.NET Core test server,
then builds the same test into an isolated arrange, act, and assert scenario. The examples are
compiled and executed from `tests/TestApi.IntegrationTests/DocumentationExamples.cs` on .NET 8,
.NET 9, and .NET 10.

## Prerequisites

Reference the application project and install the core package and xUnit v3 in the test project:

```shell
dotnet add reference ../MyApi/MyApi.csproj
dotnet add package XBullet.EasyTesting
dotnet add package xunit.v3
```

Expose the application entry point to the test project:

```csharp
public partial class Program;
```

The examples below use `TestApiFactory`, the repository's test factory. A minimal project can use
`AuthenticatedWebApplicationFactory<Program>` directly. Use a derived factory when tests need
shared service replacement, authentication-scheme mapping, database isolation, or reusable
scenario resources.

For focus, the generated snippets omit the source file's `using` directives for `System.Net`,
`XBullet.EasyTesting.Hosting`, the application model namespace, and `Xunit`.

## Send an authenticated request

Create an isolated scenario scope, build a client with the required identity, send the request,
and dispose the scope, client, and response:

<!-- snippet: tests/TestApi.IntegrationTests/DocumentationExamples.cs#docs-minimal-controller-test -->
```csharp
public sealed class MinimalControllerDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public MinimalControllerDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_user_can_call_a_protected_controller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user
                .WithName("Ada")
                .WithNameIdentifier("user-42"))
            .WithoutRedirects()
            .Build();

        using var response = await client.GetAsync(
            "/api/secure/me",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```
<!-- end-snippet -->

`AsUser` installs a simulated identity only inside the test host. `WithoutRedirects` keeps an
authentication or authorization failure visible as its original HTTP status instead of following
a redirect.

## Arrange data and assert the response

For a stateful endpoint, use the scenario scope for database setup and the fluent scenario for the
request. The scope owns test-specific services and cleanup; the result owns its client and response:

<!-- snippet: tests/TestApi.IntegrationTests/DocumentationExamples.cs#docs-realistic-controller-scenario -->
```csharp
public sealed class RealisticControllerDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public RealisticControllerDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Scenario_arranges_data_and_verifies_the_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);

        using var result = await scope.Scenario()
            .Arrange(token => _factory.Database(scope)
                .Seed(new Product
                {
                    Id = 841,
                    Name = "Desk lamp",
                    Price = 34.95m
                })
                .ExecuteAsync(token))
            .AsUser(user => user.WithName("Product reader"))
            .Get("/api/products/841")
            .ExecuteAsync(cancellationToken);

        await result.Should()
            .HaveStatusCode(HttpStatusCode.OK)
            .HaveJsonBodyAsync(
                new ProductResponse(841, "Desk lamp", 34.95m),
                cancellationToken: cancellationToken);
    }

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
```
<!-- end-snippet -->

The database arrangement runs before the request. Disposing the result releases its HTTP resources,
and asynchronously disposing the scope invokes database and scenario-resource cleanup even when an
assertion fails.

### Match application JSON conventions

`PostJson` and `PutJson` automatically use the hosted application's MVC JSON options, including
converters configured by `AddControllers().AddJsonOptions(...)`. For a minimal-API-only host, they
use its HTTP JSON options instead. Override that application convention for one scenario with
`WithJsonOptions`; a per-request options argument has the highest precedence:

```csharp
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.Converters.Add(new JsonStringEnumConverter());

using var result = await scope.Scenario()
    .WithJsonOptions(jsonOptions)
    .PostJson("/api/users", request)
    .ExecuteAsync(cancellationToken);
```

The request recorder captures the resulting wire JSON unchanged, so differences between request
and response conventions remain visible in complete exchange snapshots.

## Inspect a failure

Response assertions throw `TestHttpResponseVerificationException` with the expected and actual
values. This executable negative example requests a protected endpoint anonymously and verifies the
stable diagnostic fields:

<!-- snippet: tests/TestApi.IntegrationTests/DocumentationExamples.cs#docs-controller-failure-diagnostics -->
```csharp
public sealed class FailureDiagnosticsDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public FailureDiagnosticsDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Failed_assertion_describes_expected_and_actual_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var result = await scope.Scenario()
            .AsAnonymous()
            .Get("/api/secure/me")
            .ExecuteAsync(cancellationToken);

        var exception = Assert.Throws<TestHttpResponseVerificationException>(
            () => result.Should().HaveStatusCode(HttpStatusCode.OK));

        Assert.Contains("200 (OK)", exception.Message);
        Assert.Contains("401 (Unauthorized)", exception.Message);
    }
}
```
<!-- end-snippet -->

Use failure examples to teach the diagnostic contract. Do not copy an entire error message unless
every word is a supported public contract.

## Next steps

- Read [authentication and scenarios](../guides/authentication-and-scenarios.md) for claims, roles,
  multiple schemes, JWTs, API keys, certificates, and end-to-end authentication.
- Use the [package-selection guide](../index.md#choose-packages) to add persistence, external HTTP,
  messaging, observability, infrastructure, Azure Functions, or snapshots.
- Follow [the executable-example contribution guide](../contributing/documentation-examples.md) when
  adding or changing a snippet.
