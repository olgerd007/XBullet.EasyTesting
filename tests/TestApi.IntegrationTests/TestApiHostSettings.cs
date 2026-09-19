using XBullet.EasyTesting.Hosting;

namespace TestApi.IntegrationTests;

internal static class TestApiHostSettings
{
    internal static EasyTestHostBuilder<Program> CreateBuilder() =>
        EasyTestHost.Create<Program>().UseIsolatedStartupDatabase();

    internal static EasyTestHostBuilder<Program> UseIsolatedStartupDatabase(
        this EasyTestHostBuilder<Program> builder) =>
        builder.UseSetting("ConnectionStrings:TestApi", CreateConnectionString());

    internal static AuthenticatedWebApplicationFactory<Program> CreateIsolatedFactory() =>
        AuthenticatedWebApplicationFactory<Program>.CreateWithHostSettings(
            settings => settings["ConnectionStrings:TestApi"] = CreateConnectionString());

    private static string CreateConnectionString() =>
        $"Data Source=test-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
}
