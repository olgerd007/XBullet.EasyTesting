using System.Text;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Confluent.Kafka;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using StackExchange.Redis;
using Testcontainers.Azurite;
using Testcontainers.Kafka;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Testcontainers.ServiceBus;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Testcontainers;
using Xunit;

namespace XBullet.EasyTesting.ContainerTests;

public sealed class ContainerModuleSmokeTests
{
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

    private static Task<TestScenarioScope<Program>> CreateScopeAsync(
        AuthenticatedWebApplicationFactory<Program> factory) =>
        factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);

    private static void AssertConfiguration(
        TestScenarioScope<Program> scope,
        string key,
        string expected) =>
        Assert.Equal(
            expected,
            scope.Services.GetRequiredService<IConfiguration>()[key]);
}
