using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Snapshots;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class JsonSnapshotExamples : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public JsonSnapshotExamples(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Raw_json_matches_a_json_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var json = $$"""
            {
              "orderId": 42,
              "status": "ready",
              "correlationId": "{{Guid.NewGuid()}}"
            }
            """;
        var settings = new SnapshotSettings()
            .ScrubGuids();

        await SnapshotAssert.MatchJsonAsync(json, settings, cancellationToken);
    }

    [Fact]
    public Task Http_response_content_matches_a_json_snapshot() =>
        _factory.RunInTestScenarioScopeAsync(
            async (scope, cancellationToken) =>
            {
                await _factory.Database(scope)
                    .Seed(new Product { Id = 961, Name = "Mechanical keyboard", Price = 129.95m })
                    .ExecuteAsync(cancellationToken);
                using var client = scope.Client()
                    .AsUser(user => user.WithName("Snapshot reader"))
                    .Build();
                using var response = await client.GetAsync("/api/products/961", cancellationToken);

                response.EnsureSuccessStatusCode();
                await response.Content.ShouldMatchJsonSnapshot(
                    cancellationToken: cancellationToken);
            },
            cancellationToken: TestContext.Current.CancellationToken);
}
