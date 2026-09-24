using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
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
    public Task Authenticated_controller_matches_own_snapshot() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAuthenticatedClient(
                TestUser.Create(name: "Grace", nameIdentifier: "user-84"));
            using var response = await client.GetAsync("/api/secure/me", cancellationToken);

            await response.ShouldMatchControllerSnapshot(
                snapshotSettings: BuiltInSnapshotAudit.CreateSettings(),
                cancellationToken: cancellationToken);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);
}
