using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// An authenticated in-memory host for applications that expose a <c>Startup</c> class instead of
/// a conventional <c>Program.Main</c> entry point.
/// </summary>
/// <remarks>
/// The host is created directly with <see cref="TestServer"/> and <c>UseStartup</c>. Derived test
/// hosts can use the same authentication and scenario hooks as
/// <see cref="AuthenticatedWebApplicationFactory{TEntryPoint}"/> without executing an application
/// entry point.
/// </remarks>
public class StartupAuthenticatedWebApplicationFactory<TStartup>
    : AuthenticatedWebApplicationFactory<TStartup>
    where TStartup : class
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseContentRoot(GetStartupContentRoot());
    }

    /// <inheritdoc />
    protected override IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder => builder
                .UseStartup<TStartup>()
                .UseTestServer());

    private static string GetStartupContentRoot() =>
        Path.GetDirectoryName(typeof(TStartup).Assembly.Location)
        ?? AppContext.BaseDirectory;
}
