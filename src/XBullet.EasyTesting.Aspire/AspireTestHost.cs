namespace XBullet.EasyTesting.Aspire;

/// <summary>Creates closed-box distributed application test hosts.</summary>
public static class AspireTestHost
{
    /// <summary>Starts configuring an Aspire AppHost test.</summary>
    /// <typeparam name="TAppHost">The AppHost entry-point type used to locate and create the distributed application.</typeparam>
    /// <returns>A new mutable, single-use Aspire test host builder.</returns>
    public static AspireTestHostBuilder<TAppHost> Create<TAppHost>()
        where TAppHost : class => new();
}
