using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.EntityFrameworkCore;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Http;
using XBullet.EasyTesting.Messaging;
using XBullet.EasyTesting.Snapshots;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestApi.Data;
using TestApi.External;
using TestApi.Messaging;

namespace TestApi.IntegrationTests;

public class TestApiFactory : EntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
    private readonly SqliteConnection _connection;

    public TestApiFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        RegisterScenarioResource(
            "External HTTP",
            ExternalCatalog);
        RegisterScenarioResource(
            "Published messages",
            PublishedMessages);
    }

    public StubHttpMessageHandler ExternalCatalog { get; } = new();

    public RecordedMessageBus PublishedMessages { get; } = new();

    public TestClientBuilder<Program> SnapshotClient(
        TestScenarioScope<Program> scope,
        Action<HttpExchangeSnapshotOptions>? configure = null)
    {
        var options = new HttpExchangeSnapshotOptions();
        configure?.Invoke(options);
        return scope.Client().WithHandler(new HttpExchangeRecorder(options));
    }

    protected override void ConfigureDatabaseServices(IServiceCollection services)
    {
        services.AddDbContext<TestApiDbContext>(options => options.UseSqlite(_connection));
    }

    protected override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        TestScenarioContext context)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context.DisposeWithScenario((IAsyncDisposable)connection);
        services.AddDbContext<TestApiDbContext>(options => options.UseSqlite(connection));
    }

    protected override void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .MapAzureAd("AzureAd")
            .MapApiKey("ApiKey");
    }

    protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
        services
            .AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>()
            .ConfigurePrimaryHttpMessageHandler(() => ExternalCatalog)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.RemoveAll<IApplicationMessagePublisher>();
        services.AddSingleton<IApplicationMessagePublisher>(
            new RecordingApplicationMessagePublisher(PublishedMessages));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
