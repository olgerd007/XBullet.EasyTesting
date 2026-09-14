using System.Net;
using System.Net.Http.Json;
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
    public async Task Health_controller_allows_anonymous_requests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsAnonymous()
            .Build();

        using var response = await client.GetAsync("/health", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body!.Status);
    }

    [Fact]
    public async Task Health_controller_matches_snapshot_without_authentication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/health", cancellationToken);

        await response.ShouldMatchControllerSnapshot(cancellationToken: cancellationToken);
    }

    private sealed record HealthResponse(string Status);
}
