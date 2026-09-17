using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.EntityFrameworkCore;
using XBullet.EasyTesting.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestApi.Data;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class StartupHostTests
{
    [Fact]
    public async Task Startup_authenticated_host_runs_without_an_entry_point()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationOnlyFederationTestHost();

        using var result = await factory.Scenario()
            .AsFederatedUser(user => user.WithApiUserClaim("portfolio", "portfolio-9"))
            .Get("/federation/context")
            .ExecuteAsync(cancellationToken);
        var body = await result.Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal("injected-appsettings", body.GetProperty("configurationSource").GetString());
        Assert.Equal(0, body.GetProperty("productCount").GetInt32());
        Assert.Equal(
            TestAuthenticationDefaults.FederationAuthenticationType,
            body.GetProperty("authenticationTypes")[0].GetString());
        Assert.Equal(
            TestAuthenticationDefaults.ApiUserIdentityAuthenticationType,
            body.GetProperty("authenticationTypes")[1].GetString());
    }

    [Fact]
    public async Task Startup_host_combines_injected_configuration_federated_user_and_scenario_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new FederationTestHost();
        await using var scope = await factory.CreateTestScenarioScopeAsync(
            scenario => scenario.ConfigureConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Federation:Source"] = "scenario-appsettings"
                })),
            cancellationToken);

        await factory.ExecuteDatabaseAsync(
            scope,
            (database, _) =>
            {
                database.Products.Add(new Product
                {
                    Id = 42,
                    Name = "Scenario product",
                    Price = 12.50m
                });
                return Task.CompletedTask;
            },
            cancellationToken);

        using var result = await scope.Scenario()
            .AsFederatedUser(user => user
                .WithNameIdentifier("federation-user-42")
                .WithFederationClaim("tenant", "customer-tenant")
                .WithApiUserClaim("portfolio", "portfolio-17"))
            .Get("/federation/context")
            .ExecuteAsync(cancellationToken);
        var body = await result.Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal("scenario-appsettings", body.GetProperty("configurationSource").GetString());
        Assert.Equal(1, body.GetProperty("productCount").GetInt32());
        Assert.Equal(
            TestAuthenticationDefaults.FederationAuthenticationType,
            body.GetProperty("authenticationTypes")[0].GetString());
        Assert.Equal(
            TestAuthenticationDefaults.ApiUserIdentityAuthenticationType,
            body.GetProperty("authenticationTypes")[1].GetString());
        Assert.Equal("ApiUserIdentity", body.GetProperty("identityTypes")[1].GetString());
        Assert.Equal(
            "portfolio-17",
            body.GetProperty("apiUserPortfolio").GetString());
    }

    private sealed class FederationTestHost
        : StartupEntityFrameworkWebApplicationFactory<IntegrationTestStartup, TestApiDbContext>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"federation-tests-{Guid.NewGuid():N}";

        protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Federation:Source"] = "injected-appsettings"
            });

        protected override void ConfigureTestAuthentication(
            TestAuthenticationSchemeBuilder authentication) =>
            authentication.MapFederation("Federation");

        protected override void ConfigureDatabaseServices(IServiceCollection services) =>
            services.AddDbContext<TestApiDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName, _databaseRoot));

        protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
        {
            services.RemoveAll<ITestClaimsPrincipalFactory>();
            services.AddSingleton<ITestClaimsPrincipalFactory, FederationClaimsPrincipalFactory>();
        }

        protected override void ConfigureScenarioDatabaseServices(
            IServiceCollection services,
            TestScenarioContext context) =>
            services.AddDbContext<TestApiDbContext>(options =>
                options.UseInMemoryDatabase(
                    $"{_databaseName}-{context.ScenarioId}",
                    _databaseRoot));
    }

    private sealed class AuthenticationOnlyFederationTestHost
        : StartupAuthenticatedWebApplicationFactory<IntegrationTestStartup>
    {
        protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Federation:Source"] = "injected-appsettings"
            });

        protected override void ConfigureTestAuthentication(
            TestAuthenticationSchemeBuilder authentication) =>
            authentication.MapFederation("Federation");
    }
}

/// <summary>A Startup-style test application used without an application Program.Main.</summary>
public sealed class IntegrationTestStartup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddDbContext<TestApiDbContext>(options =>
            options.UseInMemoryDatabase("federation-production-registration"));
        services.AddAuthentication();
        services.AddAuthorization(options => options.AddPolicy(
            "FederationOnly",
            policy => policy
                .AddAuthenticationSchemes("Federation")
                .RequireAuthenticatedUser()));
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseRouting();
        application.UseAuthentication();
        application.UseAuthorization();
        application.UseEndpoints(endpoints => endpoints
            .MapGet("/federation/context", async context =>
            {
                var database = context.RequestServices.GetRequiredService<TestApiDbContext>();
                var authenticationTypes = context.User.Identities
                    .Select(identity => identity.AuthenticationType)
                    .ToArray();
                var apiUserPortfolio = context.User.Identities
                    .Skip(1)
                    .SelectMany(identity => identity.Claims)
                    .Single(claim => claim.Type == "portfolio")
                    .Value;
                var identityTypes = context.User.Identities
                    .Select(identity => identity.GetType().Name)
                    .ToArray();

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        ConfigurationSource = configuration["Federation:Source"],
                        ProductCount = await database.Products.CountAsync(context.RequestAborted),
                        AuthenticationTypes = authenticationTypes,
                        IdentityTypes = identityTypes,
                        ApiUserPortfolio = apiUserPortfolio
                    },
                    context.RequestAborted);
            })
            .RequireAuthorization("FederationOnly"));
    }
}

internal sealed class FederationClaimsPrincipalFactory : TestClaimsPrincipalFactory
{
    protected override ClaimsIdentity CreateAdditionalIdentity(TestIdentity identity) =>
        identity.AuthenticationType == TestAuthenticationDefaults.ApiUserIdentityAuthenticationType
            ? new ApiUserIdentity(
                identity.Claims.Select(claim => new Claim(claim.Type, claim.Value)),
                identity.AuthenticationType,
                identity.NameClaimType,
                identity.RoleClaimType)
            : base.CreateAdditionalIdentity(identity);
}

internal sealed class ApiUserIdentity(
    IEnumerable<Claim> claims,
    string authenticationType,
    string nameClaimType,
    string roleClaimType)
    : ClaimsIdentity(claims, authenticationType, nameClaimType, roleClaimType);
