using System.Text.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Snapshots;
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

    [Fact]
    public void Verify_conversion_preserves_every_JSON_value_kind_in_complete_exchanges()
    {
        using var document = JsonDocument.Parse(
            """{"text":"value","integer":42,"decimal":1.25,"large":1e400,"true":true,"false":false,"null":null,"items":[1,"two"]}""");
        var converted = Assert.IsType<Dictionary<string, object?>>(
            ControllerSnapshotExtensions.ToVerifyValue(document.RootElement));

        Assert.Equal("value", converted["text"]);
        Assert.Equal(42L, converted["integer"]);
        Assert.Equal(1.25m, converted["decimal"]);
        Assert.Equal("1e400", converted["large"]);
        Assert.True(Assert.IsType<bool>(converted["true"]));
        Assert.False(Assert.IsType<bool>(converted["false"]));
        Assert.Null(converted["null"]);
        Assert.Equal([1L, "two"], Assert.IsType<object[]>(converted["items"]));

        var request = new HttpExchangeRequestSnapshot("POST", "/orders", null, document.RootElement);
        var response = new HttpExchangeResponseSnapshot(200, "OK", null, document.RootElement);
        var snapshot = ControllerSnapshotExtensions.ToVerifyExchangeSnapshot(
            new HttpExchangeSnapshot(request, response, null));
        Assert.IsType<Dictionary<string, object?>>(snapshot.Request!.Body);
        Assert.IsType<Dictionary<string, object?>>(snapshot.Response!.Body);

        var unchanged = ControllerSnapshotExtensions.ToVerifyExchangeSnapshot(
            new HttpExchangeSnapshot(
                request with { Body = "plain" },
                response with { Body = null },
                null));
        Assert.Equal("plain", unchanged.Request!.Body);
        Assert.Null(unchanged.Response!.Body);
    }

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);
}
