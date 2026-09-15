using Testcontainers.Azurite;
using Testcontainers.Kafka;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Testcontainers.ServiceBus;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Testcontainers;

/// <summary>Registers supported preconfigured Testcontainers modules.</summary>
public static class TestcontainerModuleExtensions
{
    private const string PostgreSqlImage = "postgres:15.1";
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";
    private const string KafkaImage = "confluentinc/cp-kafka:7.5.12";
    private const string RedisImage = "redis:7.0";
    private const string RabbitMqImage = "rabbitmq:3.11";
    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.37.0";
    private const string ServiceBusImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.0";

    /// <summary>Adds a PostgreSQL container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UsePostgreSql<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UsePostgreSql(_ => { });

    /// <summary>Adds a configured PostgreSQL container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UsePostgreSql<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<PostgreSqlBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("PostgreSql", "ConnectionStrings:PostgreSql", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new PostgreSqlBuilder(PostgreSqlImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a PostgreSQL container to one scenario.</summary>
    public static TestScenarioScopeBuilder UsePostgreSql(this TestScenarioScopeBuilder builder) =>
        builder.UsePostgreSql(_ => { });

    /// <summary>Adds a configured PostgreSQL container to one scenario.</summary>
    public static TestScenarioScopeBuilder UsePostgreSql(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<PostgreSqlBuilder>> configure)
    {
        var options = Options("PostgreSql", "ConnectionStrings:PostgreSql", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new PostgreSqlBuilder(PostgreSqlImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a SQL Server container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseSqlServer<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseSqlServer(_ => { });

    /// <summary>Adds a configured SQL Server container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseSqlServer<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<MsSqlBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("SqlServer", "ConnectionStrings:SqlServer", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new MsSqlBuilder(SqlServerImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a SQL Server container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseSqlServer(this TestScenarioScopeBuilder builder) =>
        builder.UseSqlServer(_ => { });

    /// <summary>Adds a configured SQL Server container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseSqlServer(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<MsSqlBuilder>> configure)
    {
        var options = Options("SqlServer", "ConnectionStrings:SqlServer", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new MsSqlBuilder(SqlServerImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a Kafka container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseKafka<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseKafka(_ => { });

    /// <summary>Adds a configured Kafka container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseKafka<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<KafkaBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("Kafka", "Kafka:BootstrapServers", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new KafkaBuilder(KafkaImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetBootstrapAddress()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a Kafka container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseKafka(this TestScenarioScopeBuilder builder) =>
        builder.UseKafka(_ => { });

    /// <summary>Adds a configured Kafka container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseKafka(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<KafkaBuilder>> configure)
    {
        var options = Options("Kafka", "Kafka:BootstrapServers", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new KafkaBuilder(KafkaImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetBootstrapAddress()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a Redis container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseRedis<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseRedis(_ => { });

    /// <summary>Adds a configured Redis container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseRedis<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<RedisBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("Redis", "ConnectionStrings:Redis", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new RedisBuilder(RedisImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a Redis container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseRedis(this TestScenarioScopeBuilder builder) =>
        builder.UseRedis(_ => { });

    /// <summary>Adds a configured Redis container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseRedis(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<RedisBuilder>> configure)
    {
        var options = Options("Redis", "ConnectionStrings:Redis", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new RedisBuilder(RedisImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a RabbitMQ container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseRabbitMq<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseRabbitMq(_ => { });

    /// <summary>Adds a configured RabbitMQ container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseRabbitMq<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<RabbitMqBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("RabbitMq", "ConnectionStrings:RabbitMq", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new RabbitMqBuilder(RabbitMqImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds a RabbitMQ container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseRabbitMq(this TestScenarioScopeBuilder builder) =>
        builder.UseRabbitMq(_ => { });

    /// <summary>Adds a configured RabbitMQ container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseRabbitMq(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<RabbitMqBuilder>> configure)
    {
        var options = Options("RabbitMq", "ConnectionStrings:RabbitMq", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new RabbitMqBuilder(RabbitMqImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds an Azurite container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseAzurite<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseAzurite(_ => { });

    /// <summary>Adds a configured Azurite container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseAzurite<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<AzuriteBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("Azurite", "ConnectionStrings:AzureStorage", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new AzuriteBuilder(AzuriteImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds an Azurite container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseAzurite(this TestScenarioScopeBuilder builder) =>
        builder.UseAzurite(_ => { });

    /// <summary>Adds a configured Azurite container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseAzurite(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<AzuriteBuilder>> configure)
    {
        var options = Options("Azurite", "ConnectionStrings:AzureStorage", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new AzuriteBuilder(AzuriteImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds an Azure Service Bus emulator container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseServiceBusEmulator<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        bool acceptLicenseAgreement)
        where TEntryPoint : class =>
        builder.UseServiceBusEmulator(options => options.ConfigureBuilder(
            native => native.WithAcceptLicenseAgreement(acceptLicenseAgreement)));

    /// <summary>Adds a configured Azure Service Bus emulator container to every scenario.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseServiceBusEmulator<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestcontainerModuleOptions<ServiceBusBuilder>> configure)
        where TEntryPoint : class
    {
        var options = Options("ServiceBus", "ConnectionStrings:ServiceBus", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new ServiceBusBuilder(ServiceBusImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    /// <summary>Adds an Azure Service Bus emulator container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseServiceBusEmulator(
        this TestScenarioScopeBuilder builder,
        bool acceptLicenseAgreement) =>
        builder.UseServiceBusEmulator(options => options.ConfigureBuilder(
            native => native.WithAcceptLicenseAgreement(acceptLicenseAgreement)));

    /// <summary>Adds a configured Azure Service Bus emulator container to one scenario.</summary>
    public static TestScenarioScopeBuilder UseServiceBusEmulator(
        this TestScenarioScopeBuilder builder,
        Action<TestcontainerModuleOptions<ServiceBusBuilder>> configure)
    {
        var options = Options("ServiceBus", "ConnectionStrings:ServiceBus", configure);
        return builder.UseTestcontainer(
            options.ResourceName,
            _ => options.Apply(new ServiceBusBuilder(ServiceBusImage)).Build(),
            container => Connection(options.ConfigurationKey, container.GetConnectionString()),
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);
    }

    private static TestcontainerModuleOptions<TBuilder> Options<TBuilder>(
        string resourceName,
        string configurationKey,
        Action<TestcontainerModuleOptions<TBuilder>> configure)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TestcontainerModuleOptions<TBuilder>(resourceName, configurationKey);
        configure(options);
        options.Validate();
        return options;
    }

    private static IReadOnlyDictionary<string, string?> Connection(string key, string value) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };
}
