# Test with containerized infrastructure

Use `XBullet.EasyTesting.Testcontainers` when behavior depends on a real database, broker, cache,
or storage protocol. Each container is a scenario environment resource: it starts before the
application host, publishes configuration only after readiness, contributes bounded diagnostics on
failure, and is disposed after the host.

## Prerequisites and installation

- A running Docker-compatible container runtime.
- Permission to pull the selected images and bind local ports.
- Explicit acceptance of the Service Bus emulator license when that module is used.

```shell
dotnet add package XBullet.EasyTesting.Testcontainers
```

Import `XBullet.EasyTesting.Testcontainers` and `XBullet.EasyTesting.Hosting`. Native container
types come from the corresponding Testcontainers modules.

## Lifecycle and configuration

Add modules to an `EasyTestHost` or `TestScenarioScopeBuilder`, then create and dispose a scenario:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UsePostgreSql(options =>
    {
        options.ResourceName = "orders-db";
        options.ConfigurationKey = "ConnectionStrings:Orders";
        options.ConfigureBuilder(builder => builder.WithDatabase("orders"));
    })
    .UseRedis()
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync(cancellationToken);
var postgres = scope.GetTestcontainer<Program, PostgreSqlContainer>("orders-db");
```

Builder configuration is lazy; no container is created until the scenario starts. Registered
resources start in order, the application starts after all resources are ready, and cleanup occurs
in reverse registration order. Never cache a container endpoint before `StartAsync`: mapped ports
are known only after startup.

## Built-in modules

| Module | Registration | Default resource | Published configuration |
| --- | --- | --- | --- |
| PostgreSQL | `UsePostgreSql()` | `PostgreSql` | `ConnectionStrings:PostgreSql` |
| SQL Server | `UseSqlServer()` | `SqlServer` | `ConnectionStrings:SqlServer` |
| Kafka | `UseKafka()` | `Kafka` | `Kafka:BootstrapServers` |
| Redis | `UseRedis()` | `Redis` | `ConnectionStrings:Redis` |
| RabbitMQ | `UseRabbitMq()` | `RabbitMq` | `ConnectionStrings:RabbitMq` |
| Azurite | `UseAzurite()` | `Azurite` | `ConnectionStrings:AzureStorage` |
| Service Bus emulator | `UseServiceBusEmulator(true)` | `ServiceBus` | `ConnectionStrings:ServiceBus` |

Each option object supports `ResourceName`, `ConfigurationKey`, and one or more
`ConfigureBuilder` callbacks for native Testcontainers configuration.

### PostgreSQL

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-postgresql -->
```csharp
[Fact(Explicit = true)]
public async Task PostgreSql_is_ready_and_accepts_queries()
{
    using var factory = EasyTestHost.Create<Program>()
        .UsePostgreSql()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, PostgreSqlContainer>("PostgreSql");

    await using var connection = new NpgsqlConnection(container.GetConnectionString());
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new NpgsqlCommand("SELECT 42", connection);
    var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

    Assert.Equal(42L, Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture));
    AssertConfiguration(scope, "ConnectionStrings:PostgreSql", container.GetConnectionString());
}
```
<!-- end-snippet -->

### SQL Server

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-sqlserver -->
```csharp
[Fact(Explicit = true)]
public async Task SqlServer_is_ready_and_accepts_queries()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseSqlServer()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, MsSqlContainer>("SqlServer");

    await using var connection = new SqlConnection(container.GetConnectionString());
    await connection.OpenAsync(TestContext.Current.CancellationToken);
    await using var command = new SqlCommand("SELECT 42", connection);
    var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

    Assert.Equal(42, Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture));
    AssertConfiguration(scope, "ConnectionStrings:SqlServer", container.GetConnectionString());
}
```
<!-- end-snippet -->

### Kafka

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-kafka -->
```csharp
[Fact(Explicit = true)]
public async Task Kafka_is_ready_and_persists_a_message()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseKafka()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, KafkaContainer>("Kafka");
    var topic = $"xbullet-{Guid.NewGuid():N}";
    using var producer = new ProducerBuilder<Null, string>(new ProducerConfig
    {
        BootstrapServers = container.GetBootstrapAddress(),
        MessageTimeoutMs = 30_000
    }).Build();

    var result = await producer.ProduceAsync(
        topic,
        new Message<Null, string> { Value = "ready" },
        TestContext.Current.CancellationToken);

    Assert.Equal(PersistenceStatus.Persisted, result.Status);
    AssertConfiguration(scope, "Kafka:BootstrapServers", container.GetBootstrapAddress());
}
```
<!-- end-snippet -->

### Redis

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-redis -->
```csharp
[Fact(Explicit = true)]
public async Task Redis_is_ready_and_round_trips_a_value()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseRedis()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, RedisContainer>("Redis");
    await using var connection = await ConnectionMultiplexer.ConnectAsync(
        container.GetConnectionString());
    var database = connection.GetDatabase();
    var key = $"xbullet:{Guid.NewGuid():N}";

    Assert.True(await database.StringSetAsync(key, "ready"));
    Assert.Equal("ready", await database.StringGetAsync(key));
    AssertConfiguration(scope, "ConnectionStrings:Redis", container.GetConnectionString());
}
```
<!-- end-snippet -->

### RabbitMQ

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-rabbitmq -->
```csharp
[Fact(Explicit = true)]
public async Task RabbitMq_is_ready_and_round_trips_a_message()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseRabbitMq()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, RabbitMqContainer>("RabbitMq");
    var connectionFactory = new ConnectionFactory
    {
        Uri = new Uri(container.GetConnectionString())
    };
    await using var connection = await connectionFactory.CreateConnectionAsync(
        TestContext.Current.CancellationToken);
    await using var channel = await connection.CreateChannelAsync(
        cancellationToken: TestContext.Current.CancellationToken);
    var queue = await channel.QueueDeclareAsync(
        queue: string.Empty,
        durable: false,
        exclusive: true,
        autoDelete: true,
        cancellationToken: TestContext.Current.CancellationToken);

    await channel.BasicPublishAsync(
        exchange: string.Empty,
        routingKey: queue.QueueName,
        body: Encoding.UTF8.GetBytes("ready"),
        cancellationToken: TestContext.Current.CancellationToken);
    var delivery = await channel.BasicGetAsync(
        queue.QueueName,
        autoAck: true,
        TestContext.Current.CancellationToken);

    Assert.NotNull(delivery);
    Assert.Equal("ready", Encoding.UTF8.GetString(delivery.Body.Span));
    AssertConfiguration(scope, "ConnectionStrings:RabbitMq", container.GetConnectionString());
}
```
<!-- end-snippet -->

### Azurite

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-azurite -->
```csharp
[Fact(Explicit = true)]
public async Task Azurite_is_ready_and_round_trips_a_blob()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseAzurite()
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, AzuriteContainer>("Azurite");
    var service = new BlobServiceClient(container.GetConnectionString());
    var blobContainer = service.GetBlobContainerClient($"xbullet-{Guid.NewGuid():N}");
    await blobContainer.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
    var blob = blobContainer.GetBlobClient("ready.txt");

    await blob.UploadAsync(
        BinaryData.FromString("ready"),
        TestContext.Current.CancellationToken);
    var download = await blob.DownloadContentAsync(TestContext.Current.CancellationToken);

    Assert.Equal("ready", download.Value.Content.ToString());
    AssertConfiguration(scope, "ConnectionStrings:AzureStorage", container.GetConnectionString());
}
```
<!-- end-snippet -->

### Service Bus emulator

The emulator module rejects registration unless `acceptLicenseAgreement: true` is supplied. The
default emulator configuration provides `queue.1` for a minimal round trip:

<!-- snippet: tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs#docs-testcontainers-servicebus -->
```csharp
[Fact(Explicit = true)]
public async Task ServiceBus_emulator_is_ready_and_round_trips_a_message()
{
    using var factory = EasyTestHost.Create<Program>()
        .UseServiceBusEmulator(acceptLicenseAgreement: true)
        .Build();
    await using var scope = await CreateScopeAsync(factory);
    var container = scope.GetTestcontainer<Program, ServiceBusContainer>("ServiceBus");
    await using var client = new ServiceBusClient(container.GetConnectionString());
    await using var sender = client.CreateSender("queue.1");
    await using var receiver = client.CreateReceiver("queue.1");

    await sender.SendMessageAsync(
        new ServiceBusMessage("ready"),
        TestContext.Current.CancellationToken);
    var received = await receiver.ReceiveMessageAsync(
        maxWaitTime: TimeSpan.FromSeconds(30),
        cancellationToken: TestContext.Current.CancellationToken);

    Assert.NotNull(received);
    Assert.Equal("ready", received.Body.ToString());
    await receiver.CompleteMessageAsync(received, TestContext.Current.CancellationToken);
    AssertConfiguration(scope, "ConnectionStrings:ServiceBus", container.GetConnectionString());
}
```
<!-- end-snippet -->

## Register an arbitrary container

Use `UseTestcontainer` for a native Testcontainers module or an `IContainer`. Resolve mapped ports
inside the configuration callback, which runs after readiness:

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
        ["Search:Endpoint"] =
            $"http://{container.Hostname}:{container.GetMappedPublicPort(7700)}"
    },
    configureServices: (container, services) => services.AddSingleton(container),
    maximumDiagnosticCharacters: 20_000);
```

The generic registration integrates with host configuration, typed lookup, and automatic cleanup:

<!-- snippet: tests/TestApi.IntegrationTests/TestcontainerTests.cs#docs-testcontainer-custom-module -->
```csharp
[Fact]
public async Task Generic_registration_integrates_with_scenario_host_and_typed_lookup()
{
    var container = CreateContainer(out var proxy);
    using var factory = TestApiHostSettings.CreateBuilder()
        .UseTestcontainer(
            "dependency",
            _ => container,
            _ => new Dictionary<string, string?>
            {
                ["Dependency:Endpoint"] = "localhost:54321"
            },
            configureServices: null,
            maximumDiagnosticCharacters: 100)
        .Build();

    await using (var scope = await factory.CreateTestScenarioScopeAsync(
        cancellationToken: TestContext.Current.CancellationToken))
    {
        Assert.Equal(
            "localhost:54321",
            scope.Services.GetRequiredService<IConfiguration>()["Dependency:Endpoint"]);
        Assert.Same(
            container,
            scope.GetTestcontainer<Program, IContainer>("dependency"));
        Assert.Equal(1, proxy.StartCount);
        Assert.Equal(0, proxy.DisposeCount);
    }

    Assert.Equal(1, proxy.DisposeCount);
}
```
<!-- end-snippet -->

Use a native wait strategy that proves the service is usable, not merely that its process started.
Readiness failures stop scenario startup and still dispose resources that started successfully.

## Resource sharing and isolation

The default model is one container set per scenario. This gives strong isolation and predictable
cleanup but costs startup time. To share infrastructure, keep a factory alive across tests and make
application data unique per scenario—for example, separate schemas, database names, topic names,
queue names, or key prefixes. Serialize scenarios that mutate the same shared resource.

Do not share a mutable container implicitly through static state. If sharing is intentional,
document ownership and ensure the final fixture disposes it even when setup fails.

## Diagnostics and failure behavior

Container diagnostics include identity, image, state, health, mapped ports, and bounded stdout and
stderr. Published configuration values are excluded because connection strings may contain
credentials. The resource cannot publish configuration before startup and disposal is idempotent:

<!-- snippet: tests/TestApi.IntegrationTests/TestcontainerTests.cs#docs-testcontainer-resource-lifecycle -->
```csharp
[Fact]
public async Task Resource_waits_for_start_before_publishing_configuration_and_services()
{
    var container = CreateContainer(out var proxy);
    var resource = new TestcontainerResource<IContainer>(
        container,
        _ => new Dictionary<string, string?>
        {
            ["ConnectionStrings:Dependency"] = "server=secret"
        },
        (_, services) => services.AddSingleton<Marker>(),
        maximumDiagnosticCharacters: 5);
    var configurationBuilder = new ConfigurationBuilder();

    Assert.Throws<InvalidOperationException>(() =>
        resource.ConfigureConfiguration(configurationBuilder));

    await resource.StartAsync(TestContext.Current.CancellationToken);
    resource.ConfigureConfiguration(configurationBuilder);
    var services = new ServiceCollection();
    resource.ConfigureServices(services);

    Assert.Equal("server=secret", configurationBuilder.Build()["ConnectionStrings:Dependency"]);
    await using var serviceProvider = services.BuildServiceProvider();
    Assert.Same(container, serviceProvider.GetRequiredService<IContainer>());
    Assert.NotNull(serviceProvider.GetService<Marker>());

    var diagnostics = JsonSerializer.Serialize(
        await resource.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken));
    Assert.Contains("container-id", diagnostics);
    Assert.Contains("defgh", diagnostics);
    Assert.DoesNotContain("abcdefgh", diagnostics);
    Assert.DoesNotContain("server=secret", diagnostics);

    await resource.DisposeAsync();
    await resource.DisposeAsync();
    Assert.Equal(1, proxy.StartCount);
    Assert.Equal(1, proxy.DisposeCount);
}
```
<!-- end-snippet -->

If startup times out, first inspect the container runtime, image pull permissions, port availability,
and the configured wait strategy. If the application starts but cannot connect, confirm that it uses
the published configuration key rather than a development value. Increase diagnostic bounds only
when the missing log tail is needed; avoid exposing secrets in container output.

## Run the real-service smoke tests

The seven smoke tests are explicit so an ordinary test run and CI do not require Docker:

```shell
dotnet test tests/XBullet.EasyTesting.ContainerTests/XBullet.EasyTesting.ContainerTests.csproj \
  --configuration Release --explicit only
```

Run them locally or in a dedicated infrastructure job. See
[`ContainerModuleSmokeTests`](../../tests/XBullet.EasyTesting.ContainerTests/ContainerModuleSmokeTests.cs)
for the canonical client round trips and
[`TestcontainerTests`](../../tests/TestApi.IntegrationTests/TestcontainerTests.cs) for lifecycle and
failure coverage.

Browse the [Testcontainers API reference](../api/packages/xbullet-easytesting-testcontainers.md).
