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
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.PostAsJsonAsync(
                "/api/products",
                new ProductRequest("Webcam", 79.95m),
                cancellationToken);

            await response.ShouldMatchControllerSnapshot(
                new ControllerSnapshotOptions().IgnoringHeaders("Location"),
                BuiltInSnapshotAudit.CreateSettings(settings => settings.ScrubMember("id")),
                cancellationToken);
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
