using System.Net.Http.Json;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Snapshots;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class CrudControllerSnapshotTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public CrudControllerSnapshotTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task List_matches_custom_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(
                    new Product { Id = 912, Name = "Mouse", Price = 45m },
                    new Product { Id = 911, Name = "Keyboard", Price = 120m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.GetAsync("/api/products", cancellationToken);

            await response.ShouldMatchControllerSnapshot(
                snapshotSettings: BuiltInSnapshotAudit.CreateSettings(),
                cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Create_matches_custom_snapshot_with_generated_id_scrubbed() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = _factory.SnapshotClient(
                    scope,
                    options => options.Response.IgnoringHeaders("Location"))
                .AsUser(user => user.WithName("CRUD snapshot tester"))
                .Build();

            using var response = await client.PostAsJsonAsync(
                "/api/products",
                new ProductRequest("Webcam", 79.95m),
                cancellationToken);

            await response.ShouldMatchHttpExchangeSnapshot(
                snapshotSettings: BuiltInSnapshotAudit.CreateSettings(
                    settings => settings.ScrubMember("id")),
                cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Update_matches_custom_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 921, Name = "Old name", Price = 10m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.PutAsJsonAsync(
                "/api/products/921",
                new ProductRequest("Updated name", 25.50m),
                cancellationToken);

            await response.ShouldMatchControllerSnapshot(
                snapshotSettings: BuiltInSnapshotAudit.CreateSettings(),
                cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Update_matches_full_exchange_with_nested_request_and_path_scrubbing() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 922, Name = "Before", Price = 10m })
                .ExecuteAsync(cancellationToken);
            using var client = _factory.SnapshotClient(
                    scope,
                    options => options.Request.RedactingHeader("X-Request-Secret"))
                .AsUser(user => user.WithName("CRUD snapshot tester"))
                .WithHeader("X-Request-Secret", "request-secret-value")
                .Build();

            using var response = await client.PutAsJsonAsync(
                "/api/products/922",
                new
                {
                    Name = "Updated with nested metadata",
                    Price = 27.50m,
                    Customer = new
                    {
                        Id = 701,
                        Contact = new
                        {
                            Email = "private@example.test",
                            Locale = "en-US"
                        }
                    }
                },
                cancellationToken);

            var settings = BuiltInSnapshotAudit.CreateSettings(settings => settings
                .ScrubMember("email")
                .ScrubPath("/Request/Body/customer/id")
                .ReplacePath("/Request/Url", "/api/products/{id}"));

            await response.ShouldMatchHttpExchangeSnapshot(
                snapshotSettings: settings,
                cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Delete_matches_custom_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 931, Name = "Disposable", Price = 5m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.DeleteAsync(
                "/api/products/931",
                cancellationToken);

            await response.ShouldMatchControllerSnapshot(
                snapshotSettings: BuiltInSnapshotAudit.CreateSettings(),
                cancellationToken: cancellationToken);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private static HttpClient CreateAuthenticatedClient(TestScenarioScope<Program> scope) =>
        scope.Client()
            .AsUser(user => user.WithName("CRUD snapshot tester"))
            .Build();

    private sealed record ProductRequest(string Name, decimal Price);
}
