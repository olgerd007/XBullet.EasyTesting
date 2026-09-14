using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Describes the identity attached to one integration-test request.</summary>
public sealed record TestUser
{
    /// <summary>Gets the application authentication scheme this identity targets.</summary>
    public string AuthenticationScheme { get; init; } = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Gets the authentication type exposed by the resulting claims identity.</summary>
    public string AuthenticationType { get; init; } = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Gets the value exposed through <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public string NameIdentifier { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets the authenticated user's display name.</summary>
    public string Name { get; init; } = "integration-test-user";

    /// <summary>Gets the roles used by role-based authorization.</summary>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>Gets additional claims used by policy-based authorization.</summary>
    public IReadOnlyCollection<TestClaim> Claims { get; init; } = Array.Empty<TestClaim>();

    /// <summary>Creates a test user with optional roles and claims.</summary>
    public static TestUser Create(
        string name = "integration-test-user",
        string? nameIdentifier = null,
        IEnumerable<string>? roles = null,
        IEnumerable<TestClaim>? claims = null,
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme,
        string authenticationType = TestAuthenticationDefaults.AuthenticationScheme) =>
        new()
        {
            AuthenticationScheme = authenticationScheme,
            AuthenticationType = authenticationType,
            Name = name,
            NameIdentifier = nameIdentifier ?? Guid.NewGuid().ToString("N"),
            Roles = roles?.ToArray() ?? Array.Empty<string>(),
            Claims = claims?.ToArray() ?? Array.Empty<TestClaim>()
        };

    /// <summary>Starts a fluent test-user definition.</summary>
    public static TestUserBuilder CreateBuilder() => new();

    internal IEnumerable<Claim> ToClaims()
    {
        yield return new Claim(ClaimTypes.NameIdentifier, NameIdentifier);
        yield return new Claim(ClaimTypes.Name, Name);

        foreach (var role in Roles)
        {
            yield return new Claim(ClaimTypes.Role, role);
        }

        foreach (var claim in Claims)
        {
            yield return new Claim(claim.Type, claim.Value);
        }
    }
}

/// <summary>A serializable claim used to construct a <see cref="TestUser"/>.</summary>
public sealed record TestClaim(string Type, string Value);
