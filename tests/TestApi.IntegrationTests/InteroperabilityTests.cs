using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.EntityFrameworkCore;
using XBullet.EasyTesting.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class InteroperabilityTests
{
    [Fact]
    public async Task Preserved_default_scheme_supports_real_and_simulated_authentication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new HybridAuthenticationFactory();

        using var real = await factory.Scenario()
            .WithBearerToken("credential")
            .Get("/hybrid/default")
            .ExecuteAsync(cancellationToken);

        using var simulated = await factory.Scenario()
            .AsUser(user => user.WithName("Persisted user"), authenticationScheme: "IntegrationTest")
            .Get("/hybrid/simulated")
            .ExecuteAsync(cancellationToken);

        using var wrongScheme = await factory.Scenario()
            .AsUser(user => user.WithName("Persisted user"), authenticationScheme: "IntegrationTest")
            .Get("/hybrid/default")
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, real.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, simulated.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongScheme.Response.StatusCode);
    }

    [Fact]
    public async Task Hybrid_default_scheme_supports_real_and_simulated_authentication_on_same_endpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new HybridDefaultAuthenticationFactory();

        using var real = await factory.Scenario()
            .WithBearerToken("credential")
            .Get("/hybrid/default")
            .ExecuteAsync(cancellationToken);

        using var simulated = await factory.Scenario()
            .AsUser(user => user.WithName("Persisted user"), authenticationScheme: "IntegrationTest")
            .Get("/hybrid/default")
            .ExecuteAsync(cancellationToken);

        using var challenged = await factory.Scenario()
            .Get("/hybrid/default")
            .ExecuteAsync(cancellationToken);

        using var forbidden = await factory.Scenario()
            .AsUser(user => user.WithName("Persisted user"), authenticationScheme: "IntegrationTest")
            .Get("/hybrid/administrator")
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, real.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, simulated.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, challenged.Response.StatusCode);
        Assert.Equal("Real", challenged.Response.Headers.GetValues("X-Challenge-Scheme").Single());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Response.StatusCode);
        Assert.Equal("Real", forbidden.Response.Headers.GetValues("X-Forbid-Scheme").Single());
    }

    [Fact]
    public void Host_settings_override_values_read_during_minimal_startup()
    {
        const string connectionString = "Data Source=early-settings;Mode=Memory;Cache=Shared";
        using var factory = AuthenticatedWebApplicationFactory<Program>.CreateWithHostSettings(
            settings => settings["ConnectionStrings:TestApi"] = connectionString);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestApi.Data.TestApiDbContext>();

        Assert.Equal(connectionString, database.Database.GetConnectionString());
    }

    [Fact]
    public async Task Identity_helpers_seed_and_link_a_persisted_user()
    {
        var manager = new RecordingUserManager();
        var services = new ServiceCollection()
            .AddSingleton<UserManager<IdentityUser>>(manager)
            .BuildServiceProvider();
        var identityUser = new IdentityUser("ada") { Id = "identity-42" };

        await services.SeedIdentityUserAsync(
            identityUser,
            password: "correct horse battery staple",
            roles: ["Administrator"]);
        var testUser = await services.CreateIdentityTestUserAsync(
            identityUser,
            authenticationScheme: "IntegrationTest");

        Assert.Same(identityUser, manager.CreatedUser);
        Assert.Equal("correct horse battery staple", manager.Password);
        Assert.Equal(["Administrator"], manager.AssignedRoles);
        Assert.Equal("identity-42", testUser.NameIdentifier);
        Assert.Equal("ada", testUser.Name);
        Assert.Equal(["Administrator"], testUser.Roles);
        Assert.Contains(testUser.Claims, claim => claim.Type == "department" && claim.Value == "research");
    }

    [Fact]
    public async Task Identity_helper_supports_identity_api_endpoints_without_roles_or_claims()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddIdentityApiEndpoints<IdentityUser>()
            .AddUserStore<NoOpUserStore>();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<UserManager<IdentityUser>>();
        var identityUser = new IdentityUser("ada") { Id = "identity-42" };

        var testUser = await manager.CreateTestUserAsync(identityUser);

        Assert.False(manager.SupportsUserRole);
        Assert.False(manager.SupportsUserClaim);
        Assert.Empty(testUser.Roles);
        Assert.Empty(testUser.Claims);
    }

    [Fact]
    public async Task DbContext_factory_registration_is_replaced_and_custom_lifecycle_runs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ContextFactoryTestHost();
        await using var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);

        var registrations = scope.Services.GetServices<IDbContextFactory<FactoryDbContext>>().ToArray();
        var connectionString = await factory.QueryDatabaseAsync(
            scope,
            (database, _) => Task.FromResult(database.Database.GetConnectionString()),
            cancellationToken);

        Assert.Single(registrations);
        Assert.Equal(1, factory.InitializationCount);
        Assert.Contains("test-context-factory", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_scenario_accepts_a_custom_recreation_callback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ContextFactoryTestHost();
        var invoked = false;

        await factory.Database()
            .RecreateDatabaseWith(async (database, token) =>
            {
                invoked = true;
                await database.Database.EnsureCreatedAsync(token);
            })
            .ExecuteAsync(cancellationToken);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Sqlite_file_scenario_is_deleted_during_cleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"xbullet-sqlite-cleanup-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new SqliteFileTestHost(databasePath);
            await using (var scope = await factory.CreateTestScenarioScopeAsync(
                cancellationToken: cancellationToken))
            {
                Assert.True(File.Exists(databasePath));
            }

            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class HybridAuthenticationFactory
        : StartupAuthenticatedWebApplicationFactory<HybridAuthenticationStartup>
    {
        protected override void ConfigureTestAuthentication(
            TestAuthenticationSchemeBuilder authentication) =>
            authentication
                .PreserveDefaultAuthenticationScheme()
                .MapTestAuthentication("IntegrationTest");
    }

    private sealed class HybridDefaultAuthenticationFactory
        : StartupAuthenticatedWebApplicationFactory<HybridAuthenticationStartup>
    {
        protected override void ConfigureTestAuthentication(
            TestAuthenticationSchemeBuilder authentication) =>
            authentication.UseHybridDefaultAuthentication("IntegrationTest");
    }

    private sealed class ContextFactoryTestHost
        : StartupEntityFrameworkWebApplicationFactory<ContextFactoryStartup, FactoryDbContext>
    {
        public int InitializationCount { get; private set; }

        protected override void ConfigureDatabaseServices(IServiceCollection services) =>
            services.AddDbContextFactory<FactoryDbContext>(options =>
                options.UseSqlite("Data Source=test-context-factory;Mode=Memory;Cache=Shared"));

        protected override async Task InitializeScenarioDatabaseAsync(
            FactoryDbContext database,
            CancellationToken cancellationToken)
        {
            InitializationCount++;
            await database.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private sealed class SqliteFileTestHost(string databasePath)
        : StartupEntityFrameworkWebApplicationFactory<ContextFactoryStartup, FactoryDbContext>
    {
        protected override void ConfigureDatabaseServices(IServiceCollection services) =>
            services.AddDbContextFactory<FactoryDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
    }

    private sealed class RecordingUserManager : UserManager<IdentityUser>
    {
        public RecordingUserManager()
            : base(
                new RoleAndClaimUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<IdentityUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                EmptyServiceProvider.Instance,
                NullLogger<UserManager<IdentityUser>>.Instance)
        {
        }

        public IdentityUser? CreatedUser { get; private set; }

        public string? Password { get; private set; }

        public IReadOnlyCollection<string> AssignedRoles { get; private set; } = [];

        public override Task<IdentityResult> CreateAsync(IdentityUser user, string password)
        {
            CreatedUser = user;
            Password = password;
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> AddToRolesAsync(
            IdentityUser user,
            IEnumerable<string> roles)
        {
            AssignedRoles = roles.ToArray();
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<string> GetUserIdAsync(IdentityUser user) => Task.FromResult(user.Id);

        public override Task<string?> GetUserNameAsync(IdentityUser user) =>
            Task.FromResult(user.UserName);

        public override Task<IList<string>> GetRolesAsync(IdentityUser user) =>
            Task.FromResult<IList<string>>(["Administrator"]);

        public override Task<IList<Claim>> GetClaimsAsync(IdentityUser user) =>
            Task.FromResult<IList<Claim>>([new Claim("department", "research")]);
    }

    private class NoOpUserStore : IUserStore<IdentityUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(
            IdentityUser user,
            string? userName,
            CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            IdentityUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityUser?> FindByIdAsync(
            string userId,
            CancellationToken cancellationToken) => Task.FromResult<IdentityUser?>(null);

        public Task<IdentityUser?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken) => Task.FromResult<IdentityUser?>(null);
    }

    private sealed class RoleAndClaimUserStore :
        NoOpUserStore,
        IUserRoleStore<IdentityUser>,
        IUserClaimStore<IdentityUser>
    {
        public Task AddToRoleAsync(
            IdentityUser user,
            string roleName,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveFromRoleAsync(
            IdentityUser user,
            string roleName,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IList<string>> GetRolesAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult<IList<string>>([]);

        public Task<bool> IsInRoleAsync(
            IdentityUser user,
            string roleName,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IList<IdentityUser>> GetUsersInRoleAsync(
            string roleName,
            CancellationToken cancellationToken) => Task.FromResult<IList<IdentityUser>>([]);

        public Task<IList<Claim>> GetClaimsAsync(
            IdentityUser user,
            CancellationToken cancellationToken) => Task.FromResult<IList<Claim>>([]);

        public Task AddClaimsAsync(
            IdentityUser user,
            IEnumerable<Claim> claims,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReplaceClaimAsync(
            IdentityUser user,
            Claim claim,
            Claim newClaim,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveClaimsAsync(
            IdentityUser user,
            IEnumerable<Claim> claims,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IList<IdentityUser>> GetUsersForClaimAsync(
            Claim claim,
            CancellationToken cancellationToken) => Task.FromResult<IList<IdentityUser>>([]);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}

public sealed class HybridAuthenticationStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services
            .AddAuthentication("Real")
            .AddScheme<AuthenticationSchemeOptions, RealAuthenticationHandler>("Real", _ => { });
        services.AddAuthorization(options => options.AddPolicy(
            "Simulated",
            policy => policy
                .AddAuthenticationSchemes("IntegrationTest")
                .RequireAuthenticatedUser()));
        services.AddAuthorizationBuilder().AddPolicy(
            "Administrator",
            policy => policy.RequireRole("Administrator"));
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseRouting();
        application.UseAuthentication();
        application.UseAuthorization();
        application.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/hybrid/default", context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }).RequireAuthorization();
            endpoints.MapGet("/hybrid/simulated", context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }).RequireAuthorization("Simulated");
            endpoints.MapGet("/hybrid/administrator", context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }).RequireAuthorization("Administrator");
        });
    }
}

public sealed class RealAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public RealAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization != "Bearer credential")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "real-user")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["X-Challenge-Scheme"] = Scheme.Name;
        return base.HandleChallengeAsync(properties);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Headers["X-Forbid-Scheme"] = Scheme.Name;
        return base.HandleForbiddenAsync(properties);
    }
}

public sealed class ContextFactoryStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddDbContextFactory<FactoryDbContext>(options =>
            options.UseSqlite("Data Source=application-context-factory;Mode=Memory;Cache=Shared"));
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseRouting();
    }
}

public sealed class FactoryDbContext(DbContextOptions<FactoryDbContext> options) : DbContext(options);
