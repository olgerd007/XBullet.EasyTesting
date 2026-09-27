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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create a PostgreSQL container named <c>PostgreSql</c> per
    /// scenario and publish its connection string as <c>ConnectionStrings:PostgreSql</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UsePostgreSql<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UsePostgreSql(_ => { });

    /// <summary>Adds a configured PostgreSQL container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native PostgreSQL builder transformations. Native transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned PostgreSQL container whose connection
    /// string is published only after the native readiness strategy succeeds.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>PostgreSql</c> and configuration key
    /// <c>ConnectionStrings:PostgreSql</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UsePostgreSql(this TestScenarioScopeBuilder builder) =>
        builder.UsePostgreSql(_ => { });

    /// <summary>Adds a configured PostgreSQL container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native PostgreSQL builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned PostgreSQL container whose connection
    /// string is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create a SQL Server container named <c>SqlServer</c> per
    /// scenario and publish its connection string as <c>ConnectionStrings:SqlServer</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseSqlServer<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseSqlServer(_ => { });

    /// <summary>Adds a configured SQL Server container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native SQL Server builder transformations. Transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned SQL Server container whose connection
    /// string is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>SqlServer</c> and configuration key
    /// <c>ConnectionStrings:SqlServer</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseSqlServer(this TestScenarioScopeBuilder builder) =>
        builder.UseSqlServer(_ => { });

    /// <summary>Adds a configured SQL Server container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native SQL Server builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned SQL Server container whose connection
    /// string is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create a Kafka container named <c>Kafka</c> per scenario and
    /// publish its bootstrap address as <c>Kafka:BootstrapServers</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseKafka<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseKafka(_ => { });

    /// <summary>Adds a configured Kafka container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native Kafka builder transformations. Transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Kafka container whose bootstrap address
    /// is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>Kafka</c> and configuration key
    /// <c>Kafka:BootstrapServers</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseKafka(this TestScenarioScopeBuilder builder) =>
        builder.UseKafka(_ => { });

    /// <summary>Adds a configured Kafka container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native Kafka builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Kafka container whose bootstrap address
    /// is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create a Redis container named <c>Redis</c> per scenario and
    /// publish its connection string as <c>ConnectionStrings:Redis</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseRedis<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseRedis(_ => { });

    /// <summary>Adds a configured Redis container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native Redis builder transformations. Transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Redis container whose connection string
    /// is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>Redis</c> and configuration key
    /// <c>ConnectionStrings:Redis</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseRedis(this TestScenarioScopeBuilder builder) =>
        builder.UseRedis(_ => { });

    /// <summary>Adds a configured Redis container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native Redis builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Redis container whose connection string
    /// is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create a RabbitMQ container named <c>RabbitMq</c> per scenario
    /// and publish its connection string as <c>ConnectionStrings:RabbitMq</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseRabbitMq<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseRabbitMq(_ => { });

    /// <summary>Adds a configured RabbitMQ container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native RabbitMQ builder transformations. Transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned RabbitMQ container whose connection
    /// string is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>RabbitMq</c> and configuration key
    /// <c>ConnectionStrings:RabbitMq</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseRabbitMq(this TestScenarioScopeBuilder builder) =>
        builder.UseRabbitMq(_ => { });

    /// <summary>Adds a configured RabbitMQ container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native RabbitMQ builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned RabbitMQ container whose connection
    /// string is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, configured to create an Azurite container named <c>Azurite</c> per scenario
    /// and publish its connection string as <c>ConnectionStrings:AzureStorage</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseAzurite<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        builder.UseAzurite(_ => { });

    /// <summary>Adds a configured Azurite container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native Azurite builder transformations. Transformations run once per scenario.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Azurite container whose connection
    /// string is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, configured with resource name <c>Azurite</c> and configuration key
    /// <c>ConnectionStrings:AzureStorage</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseAzurite(this TestScenarioScopeBuilder builder) =>
        builder.UseAzurite(_ => { });

    /// <summary>Adds a configured Azurite container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native Azurite builder
    /// transformations. Transformations run once when the scenario creates its container.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Azurite container whose connection
    /// string is published after readiness.
    /// </returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="acceptLicenseAgreement">
    /// <see langword="true"/> only when the caller accepts the emulator's license terms;
    /// <see langword="false"/> leaves them unaccepted and can prevent container startup.
    /// </param>
    /// <returns>
    /// The same builder, configured to create a Service Bus emulator named <c>ServiceBus</c> per
    /// scenario and publish its connection string as <c>ConnectionStrings:ServiceBus</c>.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseServiceBusEmulator<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        bool acceptLicenseAgreement)
        where TEntryPoint : class =>
        builder.UseServiceBusEmulator(options => options.ConfigureBuilder(
            native => native.WithAcceptLicenseAgreement(acceptLicenseAgreement)));

    /// <summary>Adds a configured Azure Service Bus emulator container to every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change the resource name, configuration key,
    /// or add native Service Bus builder transformations. Transformations run once per scenario. The
    /// caller is responsible for configuring required license acceptance on the native builder.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Service Bus emulator whose connection
    /// string is published only after readiness.
    /// </returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="acceptLicenseAgreement">
    /// <see langword="true"/> only when the caller accepts the emulator's license terms;
    /// <see langword="false"/> leaves them unaccepted and can prevent container startup.
    /// </param>
    /// <returns>
    /// The same builder, configured with resource name <c>ServiceBus</c> and configuration key
    /// <c>ConnectionStrings:ServiceBus</c>.
    /// </returns>
    public static TestScenarioScopeBuilder UseServiceBusEmulator(
        this TestScenarioScopeBuilder builder,
        bool acceptLicenseAgreement) =>
        builder.UseServiceBusEmulator(options => options.ConfigureBuilder(
            native => native.WithAcceptLicenseAgreement(acceptLicenseAgreement)));

    /// <summary>Adds a configured Azure Service Bus emulator container to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once to change names or add native Service Bus builder
    /// transformations. Transformations run once when the scenario creates its container. The caller
    /// is responsible for configuring required license acceptance on the native builder.
    /// </param>
    /// <returns>
    /// The same builder, configured for a lazy, scenario-owned Service Bus emulator whose connection
    /// string is published after readiness.
    /// </returns>
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
