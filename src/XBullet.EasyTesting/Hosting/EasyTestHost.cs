namespace XBullet.EasyTesting.Hosting;

/// <summary>Creates composable ASP.NET Core integration-test hosts.</summary>
public static class EasyTestHost
{
    /// <summary>Starts a composable test-host definition for an application entry point.</summary>
    public static EasyTestHostBuilder<TEntryPoint> Create<TEntryPoint>()
        where TEntryPoint : class =>
        new();
}
