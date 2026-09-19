using System.Net;
using XBullet.EasyTesting.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ComposableHostTests
{
    [Fact]
    public async Task Builder_composes_multiple_authentication_modules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = TestApiHostSettings.CreateBuilder()
            .ConfigureConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EasyTesting:Source"] = "composable-builder"
                }))
            .ConfigureServices(services => services.AddSingleton(
                new HostMarker("registered")))
            .ConfigureAuthentication(authentication => authentication.MapAzureAd("AzureAd"))
            .ConfigureAuthentication(authentication => authentication.MapApiKey("ApiKey"));
        using var factory = builder.Build();
        var arranged = false;
        using var azureAdScenario = await factory.Scenario()
            .Arrange(_ =>
            {
                arranged = true;
                return Task.CompletedTask;
            })
            .AsAzureAdUser(user => user
                .WithTenantId("tenant-42")
                .WithScope("orders.read"))
            .Get("/api/secure/azure-ad")
            .ExecuteAsync(cancellationToken);
        using var apiKeyClient = factory.Client()
            .AsApiKey(apiKey => apiKey.WithKeyId("partner-key"))
            .Build();

        using var apiKeyResponse = await apiKeyClient.GetAsync(
            "/api/secure/api-key",
            cancellationToken);

        Assert.True(arranged);
        Assert.Equal(HttpStatusCode.NoContent, azureAdScenario.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, apiKeyResponse.StatusCode);
        Assert.Equal(
            "composable-builder",
            factory.Services.GetRequiredService<IConfiguration>()["EasyTesting:Source"]);
        Assert.Equal(
            "registered",
            factory.Services.GetRequiredService<HostMarker>().Value);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public async Task Scenario_requires_exactly_one_request_and_executes_only_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = TestApiHostSettings.CreateBuilder().Build();
        var missingRequest = factory.Scenario();
        var duplicateRequest = factory.Scenario().Get("/health");
        var executable = factory.Scenario().Get("/health");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => missingRequest.ExecuteAsync(cancellationToken));
        Assert.Throws<InvalidOperationException>(() => duplicateRequest.Delete("/health"));

        using var result = await executable.ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executable.ExecuteAsync(cancellationToken));
    }

    private sealed record HostMarker(string Value);
}
