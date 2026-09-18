namespace XBullet.EasyTesting.Snapshots;

internal static class ContinuousIntegrationEnvironment
{
    public static bool IsDetected() =>
        IsEnabled("CI") ||
        IsEnabled("TF_BUILD") ||
        IsEnabled("GITHUB_ACTIONS") ||
        IsSet("TEAMCITY_VERSION") ||
        IsSet("JENKINS_URL");

    public static bool IsEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static bool IsSet(string variableName) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName));
}
