using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Verify.Xunit;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ControllerSnapshotTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ControllerSnapshotTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_controller_matches_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(
            TestUser.Create(name: "Ada", nameIdentifier: "user-42"));
        using var response = await client.GetAsync("/api/secure/me", cancellationToken);

        await response.VerifyControllerSnapshot(cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task Unauthorized_controller_matches_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/api/secure/me", cancellationToken);

        await response.VerifyControllerSnapshot(cancellationToken: cancellationToken);
    }
}
