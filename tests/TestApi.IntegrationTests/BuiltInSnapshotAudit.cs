using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Snapshots;
using Xunit;

[assembly: AssemblyFixture(typeof(TestApi.IntegrationTests.BuiltInSnapshotAudit))]

namespace TestApi.IntegrationTests;

public sealed class BuiltInSnapshotAudit : IDisposable
{
    private static readonly SnapshotCatalog Catalog = new();
    private static readonly SnapshotSettingsDefaults Defaults = new(settings =>
        settings.TrackingWith(Catalog));

    public static SnapshotSettings CreateSettings(Action<SnapshotSettings>? configure = null) =>
        Defaults.Create(configure);

    public void Dispose()
    {
        if (!IsContinuousIntegration())
        {
            return;
        }

        var obsolete = Catalog.FindObsoleteSnapshots(GetSnapshotDirectory());
        if (obsolete.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Built-in snapshot files were not exercised by the complete test run:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, obsolete.Select(path => $" - {path}")));
    }

    private static string GetSnapshotDirectory([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "__snapshots__");

    private static bool IsContinuousIntegration() =>
        IsEnabled("CI") || IsEnabled("GITHUB_ACTIONS");

    private static bool IsEnabled(string variable) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variable),
            "true",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);
}
