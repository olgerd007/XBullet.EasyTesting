using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XBullet.EasyTesting.Authentication;

/// <summary>
/// Authenticates requests carrying a test-user header. Requests without the header remain anonymous.
/// This handler must only be enabled in a test host.
/// </summary>
public sealed class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationOptions>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Initializes the test authentication handler.</summary>
    public TestAuthenticationHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestAuthenticationDefaults.UserHeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail("The integration-test user header is invalid."));
        }

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(values[0]!);
            var user = JsonSerializer.Deserialize<TestUser>(bytes, SerializerOptions)
                ?? throw new JsonException("The integration-test user payload is empty.");

            if (!string.Equals(Scheme.Name, TestAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal) &&
                !string.Equals(Scheme.Name, user.AuthenticationScheme, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principalFactory = Context.RequestServices
                .GetService<ITestClaimsPrincipalFactory>()
                ?? new TestClaimsPrincipalFactory();
            var principal = principalFactory.CreatePrincipal(user);
            var properties = new AuthenticationProperties(
                new Dictionary<string, string?>(user.AuthenticationProperties));
            var ticket = new AuthenticationTicket(principal, properties, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Task.FromResult(AuthenticateResult.Fail("The integration-test user header could not be decoded."));
        }
    }
}
