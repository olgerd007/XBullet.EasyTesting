namespace XBullet.EasyTesting.Aspire;

/// <summary>Creates closed-box distributed application test hosts.</summary>
public static class AspireTestHost
{
    /// <summary>Starts configuring an Aspire AppHost test.</summary>
    public static AspireTestHostBuilder<TAppHost> Create<TAppHost>()
        where TAppHost : class => new();
}
