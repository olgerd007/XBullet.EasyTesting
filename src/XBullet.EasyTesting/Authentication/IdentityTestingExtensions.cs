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
    /// <typeparam name="TUser">
    /// The application's reference-type Identity user registered with
    /// <see cref="UserManager{TUser}"/>.
    /// </typeparam>
    /// <param name="services">
    /// The service provider used to create and dispose an asynchronous scope and resolve the user
    /// manager. The provider is not owned or disposed by this method.
    /// </param>
    /// <param name="user">
    /// The non-null user instance passed to the user manager for persistence. The method does not
    /// own or dispose it, but the configured Identity store may mutate it during creation.
    /// </param>
    /// <param name="password">
    /// The password passed to Identity user creation, or <see langword="null"/> to use the
    /// passwordless creation overload. The default is <see langword="null"/>; non-null values,
    /// including an empty string, are validated by the configured Identity services.
    /// </param>
    /// <param name="roles">
    /// Role names to materialize once and assign after successful user creation. When
    /// <see langword="null"/> or empty, no role-assignment call is made. The enumerable is read but
    /// not retained, owned, or disposed.
    /// </param>
    /// <returns>
    /// A task whose result is the same <paramref name="user"/> instance after successful creation
    /// and optional role assignment.
    /// </returns>
    /// <remarks>
    /// User creation and role assignment are separate store operations. If role assignment fails,
    /// this method throws without rolling back the already-created user.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Identity creation or role assignment fails, or no matching user manager is registered.
    /// </exception>
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
    /// <typeparam name="TUser">
    /// The application's reference-type Identity user registered with
    /// <see cref="UserManager{TUser}"/>.
    /// </typeparam>
    /// <param name="services">
    /// The service provider used to create and dispose an asynchronous scope and resolve the user
    /// manager. The provider is not owned or disposed by this method.
    /// </param>
    /// <param name="user">
    /// The non-null persisted user whose identifier, name, roles, and claims are read. The method
    /// does not own, dispose, persist, or mutate it.
    /// </param>
    /// <param name="authenticationScheme">
    /// The non-empty simulated authentication scheme stored as both the target scheme and
    /// authentication type. The default is
    /// <see cref="TestAuthenticationDefaults.AuthenticationScheme"/>.
    /// </param>
    /// <returns>
    /// A task whose result is a new test-user snapshot linked by the persisted user's identifier.
    /// The caller owns the returned definition.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="UserManager{TUser}"/> is registered in the created scope.
    /// </exception>
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
    /// <typeparam name="TUser">The reference-type Identity user managed by this user manager.</typeparam>
    /// <param name="userManager">
    /// The non-null manager used to read user data. The method does not own or dispose it.
    /// </param>
    /// <param name="user">
    /// The non-null user whose identifier, name, supported roles, and supported claims are read.
    /// The method does not own, dispose, persist, or mutate it.
    /// </param>
    /// <param name="authenticationScheme">
    /// The non-empty simulated authentication scheme stored as both the target scheme and
    /// authentication type. The default is
    /// <see cref="TestAuthenticationDefaults.AuthenticationScheme"/>.
    /// </param>
    /// <returns>
    /// A task whose result is a new test-user snapshot. Its display name is the Identity user name,
    /// or the user identifier when the name is <see langword="null"/>. Standard name, identifier,
    /// and role claims are excluded from the additional-claims collection.
    /// </returns>
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
