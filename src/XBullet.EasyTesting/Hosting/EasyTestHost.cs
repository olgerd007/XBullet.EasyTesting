namespace XBullet.EasyTesting.Hosting;

/// <summary>Creates composable ASP.NET Core integration-test hosts.</summary>
public static class EasyTestHost
{
    /// <summary>Starts a composable test-host definition for an application entry point.</summary>
    /// <typeparam name="TEntryPoint">
    /// The application entry-point type used to locate and bootstrap the ASP.NET Core application.
    /// </typeparam>
    /// <returns>A new mutable builder that has not yet created an application factory.</returns>
    public static EasyTestHostBuilder<TEntryPoint> Create<TEntryPoint>()
        where TEntryPoint : class =>
        new();
}
