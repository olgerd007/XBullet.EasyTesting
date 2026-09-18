using Microsoft.Extensions.Configuration;
using XBullet.EasyTesting.Hosting;
using TestStartupApi.Features;

namespace TestStartupApi.IntegrationTests.Scenarios;

internal static class FeatureScenarioExtensions
{
    public static TestScenarioScopeBuilder EnableFeature(
        this TestScenarioScopeBuilder scenario,
        string featureName) =>
        SetFeature(scenario, featureName, enabled: true);

    public static TestScenarioScopeBuilder DisableFeature(
        this TestScenarioScopeBuilder scenario,
        string featureName) =>
        SetFeature(scenario, featureName, enabled: false);

    private static TestScenarioScopeBuilder SetFeature(
        TestScenarioScopeBuilder scenario,
        string featureName,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        return scenario.ConfigureConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{FeatureOptions.SectionName}:{featureName}"] = enabled.ToString()
            }));
    }
}
