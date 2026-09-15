using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Snapshots;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AnonymousControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AnonymousControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Health_controller_allows_anonymous_requests() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsAnonymous()
                .Build();

            using var response = await client.GetAsync("/health", cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", body!.Status);
        });

    [Fact]
    public Task Health_controller_matches_snapshot_without_authentication() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAnonymousClient();
            using var response = await client.GetAsync("/health", cancellationToken);

            await response.ShouldMatchControllerSnapshot(cancellationToken: cancellationToken);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed record HealthResponse(string Status);
}
