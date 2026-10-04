using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class HostEnvironmentTests
{
    [Fact]
    public void Factory_defaults_to_testing_at_startup()
    {
        using var factory = TestApiHostSettings.CreateIsolatedFactory();

        AssertEnvironment(factory.Services, "Testing");

        using var fluentFactory = TestApiHostSettings.CreateBuilder().Build();
        AssertEnvironment(fluentFactory.Services, "Testing");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("ProductionLike")]
    public async Task Environment_is_visible_at_startup_and_inherited_by_scenario_hosts(
        string environmentName)
    {
        using var factory = EasyTestHost.Create<Program>()
            .UseEnvironment(environmentName)
            .UseIsolatedStartupDatabase()
            .Build();

        AssertEnvironment(factory.Services, environmentName);

        await using var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        AssertEnvironment(scope.Services, environmentName);
    }

    [Fact]
    public void Environment_settings_follow_registration_order()
    {
        using var factory = TestApiHostSettings.CreateBuilder()
            .UseEnvironment("Development")
            .ConfigureHostSettings(settings => settings[WebHostDefaults.EnvironmentKey] = "Staging")
            .UseEnvironment("Production")
            .Build();

        AssertEnvironment(factory.Services, "Production");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Environment_rejects_missing_names(string? environmentName)
    {
        var builder = EasyTestHost.Create<Program>();

        var exception = Assert.ThrowsAny<ArgumentException>(
            () => builder.UseEnvironment(environmentName!));

        Assert.Equal("environmentName", exception.ParamName);
    }

    [Fact]
    public void Environment_cannot_change_after_build()
    {
        var builder = EasyTestHost.Create<Program>();
        using var factory = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.UseEnvironment("Development"));
    }

    private static void AssertEnvironment(IServiceProvider services, string expected)
    {
        Assert.Equal(expected, services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.Equal(expected, services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        var startup = services.GetRequiredService<StartupEnvironment>();
        Assert.Equal(expected, startup.Name);
        Assert.Equal(expected == Environments.Development, startup.IsDevelopment);
    }
}
