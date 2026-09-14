using XBullet.EasyTesting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// An in-memory ASP.NET Core host whose default authentication scheme accepts per-request test users.
/// </summary>
public class AuthenticatedWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly Action<IConfigurationBuilder>? _configureConfiguration;
    private readonly Action<TestAuthenticationSchemeBuilder>? _configureAuthentication;
    private readonly object _authenticationConfigurationLock = new();
    private TestAuthenticationSchemeBuilder? _authenticationConfiguration;

    /// <summary>Creates a factory with no additional test overrides.</summary>
    public AuthenticatedWebApplicationFactory()
    {
    }

    private AuthenticatedWebApplicationFactory(
        Action<IServiceCollection>? configureServices,
        Action<IConfigurationBuilder>? configureConfiguration,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication)
    {
        _configureServices = configureServices;
        _configureConfiguration = configureConfiguration;
        _configureAuthentication = configureAuthentication;
    }

    /// <summary>Creates a factory with test service, configuration, and authentication callbacks.</summary>
    public static AuthenticatedWebApplicationFactory<TEntryPoint> Create(
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication = null) =>
        new(configureServices, configureConfiguration, configureAuthentication);

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var testAuthentication = GetAuthenticationConfiguration();
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            ConfigureTestConfiguration(configuration);
            _configureConfiguration?.Invoke(configuration);
        });

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultForbidScheme = TestAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultScheme = TestAuthenticationDefaults.AuthenticationScheme;
                })
                .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                    TestAuthenticationDefaults.AuthenticationScheme,
                    _ => { });

            foreach (var authenticationScheme in testAuthentication.AdditionalSchemes)
            {
                services.AddOptions<TestAuthenticationOptions>(authenticationScheme);
            }

            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddSingleton<IAuthenticationSchemeProvider>(provider =>
                new TestAuthenticationSchemeProvider(
                    provider.GetRequiredService<IOptions<AuthenticationOptions>>(),
                    testAuthentication.AdditionalSchemes));

            ConfigureServicesForTests(services);
            _configureServices?.Invoke(services);
        });
    }

    /// <summary>Override to add in-memory configuration values to the test application.</summary>
    protected virtual void ConfigureTestConfiguration(IConfigurationBuilder configuration)
    {
    }

    /// <summary>Override to replace application services with test doubles.</summary>
    protected virtual void ConfigureServicesForTests(IServiceCollection services)
    {
    }

    /// <summary>
    /// Override to map Azure AD, API-key, or custom test profiles to application scheme names.
    /// </summary>
    protected virtual void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
    }

    /// <summary>Creates a client that sends requests without a test identity.</summary>
    public HttpClient CreateAnonymousClient(WebApplicationFactoryClientOptions? options = null) =>
        options is null ? CreateClient() : CreateClient(options);

    /// <summary>Creates a client whose requests use the supplied test identity.</summary>
    public HttpClient CreateAuthenticatedClient(
        TestUser? user = null,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = CreateAnonymousClient(options);
        return client.AuthenticateAs(user ?? new TestUser());
    }

    /// <summary>Starts a fluent test-client definition. Clients are anonymous until <c>AsUser</c> is called.</summary>
    public TestClientBuilder<TEntryPoint> Client() => new(this);

    internal string GetAzureAdAuthenticationScheme() => GetAuthenticationConfiguration().AzureAdScheme;

    internal string GetApiKeyAuthenticationScheme() => GetAuthenticationConfiguration().ApiKeyScheme;

    private TestAuthenticationSchemeBuilder GetAuthenticationConfiguration()
    {
        lock (_authenticationConfigurationLock)
        {
            if (_authenticationConfiguration is not null)
            {
                return _authenticationConfiguration;
            }

            var authentication = new TestAuthenticationSchemeBuilder();
            ConfigureTestAuthentication(authentication);
            _configureAuthentication?.Invoke(authentication);
            _authenticationConfiguration = authentication;
            return authentication;
        }
    }
}
