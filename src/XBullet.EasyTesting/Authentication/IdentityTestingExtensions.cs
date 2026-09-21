using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Helpers for arranging persisted ASP.NET Core Identity users in integration tests.</summary>
public static class IdentityTestingExtensions
{
    /// <summary>
    /// Creates an Identity user through the application's <see cref="UserManager{TUser}"/>,
    /// optionally assigning a password and roles.
    /// </summary>
    public static async Task<TUser> SeedIdentityUserAsync<TUser>(
        this IServiceProvider services,
        TUser user,
        string? password = null,
        IEnumerable<string>? roles = null)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(user);

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var createResult = password is null
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);
        ThrowIfFailed(createResult, "create the Identity test user");

        if (roles is not null)
        {
            var roleNames = roles.ToArray();
            if (roleNames.Length > 0)
            {
                ThrowIfFailed(
                    await userManager.AddToRolesAsync(user, roleNames),
                    "assign roles to the Identity test user");
            }
        }

        return user;
    }

    /// <summary>Creates a simulated identity linked to a persisted Identity user.</summary>
    public static async Task<TestUser> CreateIdentityTestUserAsync<TUser>(
        this IServiceProvider services,
        TUser user,
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<TUser>>()
            .CreateTestUserAsync(user, authenticationScheme);
    }

    /// <summary>Creates a simulated identity linked to a user managed by ASP.NET Core Identity.</summary>
    public static async Task<TestUser> CreateTestUserAsync<TUser>(
        this UserManager<TUser> userManager,
        TUser user,
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);

        var userId = await userManager.GetUserIdAsync(user);
        var userName = await userManager.GetUserNameAsync(user);
        var roles = userManager.SupportsUserRole
            ? await userManager.GetRolesAsync(user)
            : [];
        var claims = userManager.SupportsUserClaim
            ? await userManager.GetClaimsAsync(user)
            : [];

        return TestUser.Create(
            name: userName ?? userId,
            nameIdentifier: userId,
            roles: roles,
            claims: claims
                .Where(claim =>
                    claim.Type != ClaimTypes.NameIdentifier &&
                    claim.Type != ClaimTypes.Name &&
                    claim.Type != ClaimTypes.Role)
                .Select(claim => new TestClaim(claim.Type, claim.Value)),
            authenticationScheme: authenticationScheme,
            authenticationType: authenticationScheme);
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(
            "; ",
            result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Failed to {operation}. {errors}");
    }
}
