using System.Text.Json;
using XBullet.EasyTesting.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestApi.Data;
using TestApi.External;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class TestScenarioScopeTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public TestScenarioScopeTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Scope_isolates_database_resources_services_and_configuration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var replacement = new ReplacementCatalogClient();

        await using (var first = await _factory.CreateTestScenarioScopeAsync(
            scope => scope
                .ConfigureConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Scenario:Name"] = "first"
                    }))
                .ConfigureServices(services =>
                {
                    services.RemoveAll<IExternalCatalogClient>();
                    services.AddSingleton<IExternalCatalogClient>(replacement);
                }),
            cancellationToken))
        {
            Assert.Same(replacement, first.Services.GetRequiredService<IExternalCatalogClient>());
            Assert.Equal("first", first.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);

            await _factory.WithScenarioDbContextAsync(
                first,
                async (database, token) =>
                {
                    database.Products.Add(new Product { Id = 901, Name = "Scoped", Price = 10m });
                    await database.SaveChangesAsync(token);
                },
                cancellationToken);
            var productCount = await _factory.WithScenarioDbContextAsync(
                first,
                (database, token) => database.Products.CountAsync(token),
                cancellationToken);
            Assert.Equal(1, productCount);

            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/scope")
                .Respond(System.Net.HttpStatusCode.OK);
            using var externalClient = new HttpClient(_factory.ExternalCatalog, disposeHandler: false)
            {
                BaseAddress = new Uri("https://external.example.test/")
            };
            using var response = await externalClient.GetAsync("/scope", cancellationToken);
            _factory.PublishedMessages.Record("test", "scope", new { Value = 42 });

            Assert.Equal(1, _factory.ExternalCatalog.CallCount);
            Assert.Equal(1, _factory.PublishedMessages.Count);
        }

        Assert.Equal(0, _factory.ExternalCatalog.CallCount);
        Assert.Equal(0, _factory.PublishedMessages.Count);

        await using var second = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        var secondProductCount = await _factory.WithScenarioDbContextAsync(
            second,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);

        Assert.Equal(0, secondProductCount);
        Assert.IsType<ExternalCatalogClient>(
            second.Services.GetRequiredService<IExternalCatalogClient>());
        Assert.Null(second.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);
    }

    [Fact]
    public async Task Scope_gate_covers_the_complete_test_lifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);

        var secondTask = _factory.CreateTestScenarioScopeAsync(cancellationToken: cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask;
        Assert.NotEqual(first.ScenarioId, second.ScenarioId);
    }

    [Fact]
    public async Task Failed_scope_captures_diagnostics_before_automatic_cleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _factory.RunInTestScenarioScopeAsync(
                async (_, token) =>
                {
                    _factory.ExternalCatalog
                        .When(HttpMethod.Get, "/diagnostic")
                        .Respond(System.Net.HttpStatusCode.OK);
                    using var externalClient = new HttpClient(
                        _factory.ExternalCatalog,
                        disposeHandler: false)
                    {
                        BaseAddress = new Uri("https://external.example.test/")
                    };
                    using var response = await externalClient.GetAsync("/diagnostic", token);
                    _factory.PublishedMessages.Record(
                        "test",
                        "diagnostic-events",
                        new { Reason = "failure" });

                    throw new InvalidOperationException("Expected test failure.");
                },
                cancellationToken: cancellationToken));

        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
            exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        var serialized = JsonSerializer.Serialize(diagnostics);
        Assert.Contains("External HTTP", serialized);
        Assert.Contains("/diagnostic", serialized);
        Assert.Contains("Published messages", serialized);
        Assert.Contains("diagnostic-events", serialized);
        Assert.Contains("ProviderName", serialized);
        Assert.Equal(0, _factory.ExternalCatalog.CallCount);
        Assert.Equal(0, _factory.PublishedMessages.Count);
    }

    private sealed class ReplacementCatalogClient : IExternalCatalogClient
    {
        public Task<ExternalCatalogProduct?> GetProductAsync(
            int productId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ExternalCatalogProduct?>(null);

        public Task<IReadOnlyList<ExternalCatalogProduct>> GetProductsAsync(
            string category,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExternalCatalogProduct>>([]);
    }
}
