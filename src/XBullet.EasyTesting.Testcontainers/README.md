# XBullet.EasyTesting.Testcontainers

Scenario-scoped PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, and Azure
Service Bus emulator infrastructure powered by Testcontainers for .NET.

The package targets .NET 8, .NET 9, and .NET 10 and requires a Docker-compatible container runtime.

## Install

```shell
dotnet add package XBullet.EasyTesting.Testcontainers
```

## Built-in modules

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UsePostgreSql(options =>
    {
        options.ConfigurationKey = "ConnectionStrings:Orders";
        options.ConfigureBuilder(builder => builder
            .WithDatabase("orders")
            .WithUsername("orders_test"));
    })
    .UseRedis()
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync();
var postgres = scope.GetTestcontainer<Program, PostgreSqlContainer>("PostgreSql");
```

Each module uses the native Testcontainers readiness strategy. The scenario application starts only
after every container is ready and its mapped endpoint has been added to configuration. Containers
are disposed in reverse registration order after the application host.

The default configuration keys are:

- PostgreSQL: `ConnectionStrings:PostgreSql`
- SQL Server: `ConnectionStrings:SqlServer`
- Kafka: `Kafka:BootstrapServers`
- Redis: `ConnectionStrings:Redis`
- RabbitMQ: `ConnectionStrings:RabbitMq`
- Azurite: `ConnectionStrings:AzureStorage`
- Service Bus emulator: `ConnectionStrings:ServiceBus`

Service Bus requires explicit license acceptance:

```csharp
builder.UseServiceBusEmulator(acceptLicenseAgreement: true);
```

## Arbitrary containers

Use `UseTestcontainer` for a native or custom module. Resolve configuration values only after the
container has started:

```csharp
builder.UseTestcontainer(
    "search",
    _ => new ContainerBuilder("getmeili/meilisearch:v1.12.8")
        .WithPortBinding(7700, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
            request => request.ForPort(7700).ForPath("/health")))
        .Build(),
    container => new Dictionary<string, string?>
    {
        ["Search:Endpoint"] = $"http://{container.Hostname}:{container.GetMappedPublicPort(7700)}"
    },
    configureServices: null,
    maximumDiagnosticCharacters: 20_000);
```

Failure diagnostics contain container identity, image, state, health, mapped ports, and bounded
stdout/stderr. Published configuration values are intentionally excluded because connection strings
can contain credentials.

## Runtime verification

The repository keeps real-service tests explicit so an ordinary unit-test run does not require
Docker. Run the complete PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, and Service Bus
suite with:

```shell
dotnet test tests/XBullet.EasyTesting.ContainerTests/XBullet.EasyTesting.ContainerTests.csproj \
  --configuration Release --explicit only
```

These slow tests are intentionally excluded from CI and are available for local or manually scheduled
infrastructure verification.
