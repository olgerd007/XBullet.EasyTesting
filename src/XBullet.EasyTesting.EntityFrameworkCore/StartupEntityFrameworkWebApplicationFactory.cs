using XBullet.EasyTesting.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>
/// A <c>Startup</c>-based authenticated test host with Entity Framework Core database and scenario
/// support that does not execute <c>Program.Main</c>.
/// </summary>
public abstract class StartupEntityFrameworkWebApplicationFactory<TStartup, TDbContext>
    : EntityFrameworkWebApplicationFactory<TStartup, TDbContext>
    where TStartup : class
    where TDbContext : DbContext
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
