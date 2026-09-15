using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
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
    public Task Authenticated_controller_matches_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAuthenticatedClient(
                TestUser.Create(name: "Ada", nameIdentifier: "user-42"));
            using var response = await client.GetAsync("/api/secure/me", cancellationToken);

            await response.VerifyControllerSnapshot(cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Unauthorized_controller_matches_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAnonymousClient();
            using var response = await client.GetAsync("/api/secure/me", cancellationToken);

            await response.VerifyControllerSnapshot(cancellationToken: cancellationToken);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);
}
