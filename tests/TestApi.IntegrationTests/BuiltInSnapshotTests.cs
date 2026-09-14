using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Snapshots;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class BuiltInSnapshotTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public BuiltInSnapshotTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_controller_matches_own_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(
            TestUser.Create(name: "Grace", nameIdentifier: "user-84"));
        using var response = await client.GetAsync("/api/secure/me", cancellationToken);

        await response.ShouldMatchControllerSnapshot(cancellationToken: cancellationToken);
    }

}
