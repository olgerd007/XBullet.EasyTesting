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
    /// <param name="options">
    /// The framework-owned monitor that supplies options for each registered test authentication
    /// scheme. The handler retains the monitor and does not dispose it.
    /// </param>
    /// <param name="logger">
    /// The framework-owned factory used to create handler loggers. The handler retains the factory
    /// and does not dispose it.
    /// </param>
    /// <param name="encoder">
    /// The framework-owned URL encoder used by the authentication-handler base class. The handler
    /// retains the encoder and does not dispose it.
    /// </param>
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
