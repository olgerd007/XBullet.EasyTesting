using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class IdentityHybridAuthenticationTests
{
    private const string Email = "real@example.test";
    private const string Password = "Test-password-42!";

    [Fact]
    public async Task Plain_authorize_accepts_real_and_simulated_users_per_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        var token = await LoginAsync(factory, cancellationToken);
        using var client = factory.Client().Build();

        using var realRequest = new HttpRequestMessage(HttpMethod.Get, "/identity-hybrid/me");
        realRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var realResponse = await client.SendAsync(realRequest, cancellationToken);

        using var simulatedRequest = new HttpRequestMessage(HttpMethod.Get, "/identity-hybrid/me");
        simulatedRequest.AuthenticateAs(TestUser.Create("simulated-user", "simulated-42"));
        using var simulatedResponse = await client.SendAsync(simulatedRequest, cancellationToken);

        using var nextRealRequest = new HttpRequestMessage(HttpMethod.Get, "/identity-hybrid/me");
        nextRealRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var nextRealResponse = await client.SendAsync(nextRealRequest, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, realResponse.StatusCode);
        Assert.Equal("real-42:real@example.test", await realResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, simulatedResponse.StatusCode);
        Assert.Equal("simulated-42:simulated-user", await simulatedResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, nextRealResponse.StatusCode);
        Assert.Equal("real-42:real@example.test", await nextRealResponse.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Missing_or_invalid_bearer_credentials_receive_the_real_challenge(string? token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        var builder = factory.Client().WithoutRedirects();
        if (token is not null)
        {
            builder.WithBearerToken(token);
        }

        using var client = builder.Build();
        using var response = await client.GetAsync("/identity-hybrid/me", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_authentication_modes_enforce_production_role_requirements(bool simulated)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        var builder = factory.Client().WithoutRedirects();
        if (simulated)
        {
            builder.AsUser(user => user.WithName("simulated-user"));
        }
        else
        {
            builder.WithBearerToken(await LoginAsync(factory, cancellationToken));
        }

        using var client = builder.Build();
        using var response = await client.GetAsync("/identity-hybrid/administrator", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_simulated_administrator_satisfies_the_production_role_requirement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        using var client = factory.Client()
            .AsUser(user => user.WithName("simulated-user").WithRole("Administrator"))
            .Build();
        using var response = await client.GetAsync("/identity-hybrid/administrator", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_simulated_header_selects_the_identity_when_both_credentials_are_present(bool validBearer)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        var token = validBearer ? await LoginAsync(factory, cancellationToken) : "invalid-token";
        using var client = factory.Client().WithBearerToken(token).Build();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/identity-hybrid/me");
        request.AuthenticateAs(TestUser.Create("simulated-user", "simulated-42"));
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("simulated-42:simulated-user", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("bnVsbA")]
    [InlineData("invalid-payload")]
    public async Task An_invalid_simulated_header_does_not_fall_back_to_a_valid_bearer_token(string header)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        using var client = factory.Client()
            .WithBearerToken(await LoginAsync(factory, cancellationToken))
            .WithHeader(TestAuthenticationDefaults.UserHeaderName, header)
            .Build();
        using var response = await client.GetAsync("/identity-hybrid/me", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task An_explicit_bearer_scheme_still_requires_a_real_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new IdentityHybridFactory();
        using var real = factory.Client()
            .WithBearerToken(await LoginAsync(factory, cancellationToken))
            .Build();
        using var simulated = factory.Client()
            .AsUser(user => user.WithName("simulated-user"))
            .Build();
        using var realResponse = await real.GetAsync("/identity-hybrid/bearer-only", cancellationToken);
        using var simulatedResponse = await simulated.GetAsync("/identity-hybrid/bearer-only", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, realResponse.StatusCode);
        Assert.Equal("real-42:real@example.test", await realResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, simulatedResponse.StatusCode);
        Assert.Equal("Bearer", Assert.Single(simulatedResponse.Headers.WwwAuthenticate).Scheme);
    }

    private static async Task<string> LoginAsync(
        IdentityHybridFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.Client().Build();
        using var response = await client.PostAsJsonAsync(
            "/login?useCookies=false",
            new LoginRequest { Email = Email, Password = Password },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
        Assert.NotNull(tokens);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        return tokens.AccessToken;
    }

    private sealed class IdentityHybridFactory
        : StartupAuthenticatedWebApplicationFactory<IdentityHybridStartup>
    {
        protected override void ConfigureTestAuthentication(
            TestAuthenticationSchemeBuilder authentication) =>
            authentication.UseHybridDefaultAuthentication("IntegrationTest");
    }

    public sealed class IdentityHybridStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddControllers().AddApplicationPart(typeof(IdentityHybridController).Assembly);
            services.AddIdentityApiEndpoints<IdentityUser>().AddUserStore<LoginUserStore>();
            services.AddAuthorization();
        }

        public void Configure(IApplicationBuilder application)
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints =>
            {
                endpoints.MapIdentityApi<IdentityUser>();
                endpoints.MapControllers();
            });
        }
    }

    private sealed class LoginUserStore : IUserPasswordStore<IdentityUser>
    {
        private readonly IdentityUser _user = new(Email)
        {
            Id = "real-42",
            NormalizedUserName = Email.ToUpperInvariant()
        };

        public LoginUserStore()
        {
            _user.PasswordHash = new PasswordHasher<IdentityUser>().HashPassword(_user, Password);
        }

        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            IdentityUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == _user.Id ? _user : null);

        public Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(normalizedUserName == _user.NormalizedUserName ? _user : null);

        public Task SetPasswordHashAsync(IdentityUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash is not null);
    }
}

[ApiController]
[Authorize]
[Route("identity-hybrid")]
public sealed class IdentityHybridController : ControllerBase
{
    [HttpGet("me")]
    public ContentResult Me() =>
        Content(string.Join(";", User.Identities.Select(identity =>
            $"{identity.FindFirst(ClaimTypes.NameIdentifier)?.Value}:{identity.Name}")));

    [Authorize(Roles = "Administrator")]
    [HttpGet("administrator")]
    public NoContentResult Administrator() => NoContent();

    [Authorize(AuthenticationSchemes = "Identity.Bearer")]
    [HttpGet("bearer-only")]
    public ContentResult BearerOnly() => Me();
}
