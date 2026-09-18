using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.EntityFrameworkCore;
using XBullet.EasyTesting.Messaging;
using TestStartupApi.IntegrationTests.Stubs;
using TestStartupApi.Data;
using TestStartupApi.External;
using TestStartupApi.Messaging;

namespace TestStartupApi.IntegrationTests;

public sealed class StartupApiFactory
    : StartupEntityFrameworkWebApplicationFactory<Startup, OrdersDbContext>
{
    private readonly string _databaseName = $"startup-api-{Guid.NewGuid():N}";

    public StartupApiFactory()
    {
        RegisterScenarioResource("External orders", ExternalOrders);
        RegisterScenarioResource("External customers", ExternalCustomers);
        RegisterScenarioResource("Post provider", PostProvider);
        RegisterScenarioResource("Kafka messages", KafkaMessages);
    }

    public StubExternalOrdersClient ExternalOrders { get; } = new();

    public StubExternalCustomersClient ExternalCustomers { get; } = new();

    public StubPostProviderClient PostProvider { get; } = new();

    public RecordedMessageBus KafkaMessages { get; } = new();

    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication) =>
        authentication.MapAzureAd("Bearer");

    protected override void ConfigureDatabaseServices(IServiceCollection services) =>
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));

    protected override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        XBullet.EasyTesting.Hosting.TestScenarioContext context) =>
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseInMemoryDatabase($"{_databaseName}-{context.ScenarioId}"));

    protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<IExternalOrdersClient>();
        services.AddSingleton(ExternalOrders);
        services.AddScoped<IExternalOrdersClient>(provider =>
            provider.GetRequiredService<StubExternalOrdersClient>());
        services.RemoveAll<IExternalCustomersClient>();
        services.AddSingleton(ExternalCustomers);
        services.AddScoped<IExternalCustomersClient>(provider =>
            provider.GetRequiredService<StubExternalCustomersClient>());
        services.RemoveAll<IPostProviderClient>();
        services.AddSingleton(PostProvider);
        services.AddScoped<IPostProviderClient>(provider =>
            provider.GetRequiredService<StubPostProviderClient>());
        services.RemoveAll<IKafkaPublisher>();
        services.AddSingleton<IKafkaPublisher>(new RecordingKafkaPublisher(KafkaMessages));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(new TimeProviderStub(
            new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<TimeProviderStub>());
    }

    private sealed class TimeProviderStub(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
