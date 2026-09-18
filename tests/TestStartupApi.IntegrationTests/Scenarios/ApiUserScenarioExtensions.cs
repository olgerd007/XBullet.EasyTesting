using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;

namespace TestStartupApi.IntegrationTests.Scenarios;

internal static class ApiUserScenarioExtensions
{
    public const string DefaultObjectId = "api-user-17";

    public static TestScenarioBuilder<TEntryPoint> AsApiUser<TEntryPoint>(
        this TestScenarioBuilder<TEntryPoint> scenario,
        Action<TestAzureAdUserBuilder>? configure = null)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(scenario);

        return scenario.AsAzureAdUser(user =>
        {
            user.WithObjectId(DefaultObjectId)
                .WithScope(AuthorizationScopes.OrdersWrite);
            configure?.Invoke(user);
        });
    }
}
