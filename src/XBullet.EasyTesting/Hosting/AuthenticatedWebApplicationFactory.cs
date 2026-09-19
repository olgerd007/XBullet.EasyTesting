using XBullet.EasyTesting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    private readonly Action<TestScenarioEnvironmentBuilder>? _configureEnvironment;
    private readonly Action<IDictionary<string, string>>? _configureHostSettings;
    private readonly object _authenticationConfigurationLock = new();
    private readonly object _scenarioResourcesLock = new();
    private readonly SemaphoreSlim _scenarioGate = new(1, 1);
    private readonly List<TestScenarioResourceRegistration> _scenarioResources = [];
    private TestAuthenticationSchemeBuilder? _authenticationConfiguration;

    /// <summary>Creates a factory with no additional test overrides.</summary>
    public AuthenticatedWebApplicationFactory()
    {
    }

    private AuthenticatedWebApplicationFactory(
        Action<IServiceCollection>? configureServices,
        Action<IConfigurationBuilder>? configureConfiguration,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication,
        Action<TestScenarioEnvironmentBuilder>? configureEnvironment,
        Action<IDictionary<string, string>>? configureHostSettings)
    {
        _configureServices = configureServices;
        _configureConfiguration = configureConfiguration;
        _configureAuthentication = configureAuthentication;
        _configureEnvironment = configureEnvironment;
        _configureHostSettings = configureHostSettings;
    }

    /// <summary>Creates a factory with test service, configuration, and authentication callbacks.</summary>
    public static AuthenticatedWebApplicationFactory<TEntryPoint> Create(
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication = null) =>
        new(
            configureServices,
            configureConfiguration,
            configureAuthentication,
            null,
            null);

    /// <summary>Creates a factory with settings available to minimal-hosting startup code.</summary>
    public static AuthenticatedWebApplicationFactory<TEntryPoint> CreateWithHostSettings(
        Action<IDictionary<string, string>> configureHostSettings,
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication = null)
    {
        ArgumentNullException.ThrowIfNull(configureHostSettings);
        return new(
            configureServices,
            configureConfiguration,
            configureAuthentication,
            null,
            configureHostSettings);
    }

    internal static AuthenticatedWebApplicationFactory<TEntryPoint> CreateWithEnvironment(
        Action<IServiceCollection>? configureServices,
        Action<IConfigurationBuilder>? configureConfiguration,
        Action<TestAuthenticationSchemeBuilder>? configureAuthentication,
        Action<TestScenarioEnvironmentBuilder>? configureEnvironment,
        Action<IDictionary<string, string>>? configureHostSettings = null) =>
        new(
            configureServices,
            configureConfiguration,
            configureAuthentication,
            configureEnvironment,
            configureHostSettings);

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var testAuthentication = GetAuthenticationConfiguration();
        var hostSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ConfigureTestHostSettings(hostSettings);
        _configureHostSettings?.Invoke(hostSettings);
        foreach (var setting in hostSettings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            ConfigureTestConfiguration(configuration);
            _configureConfiguration?.Invoke(configuration);
        });

        builder.ConfigureTestServices(services =>
        {
            var defaultScheme = testAuthentication.DefaultEndToEndScheme
                ?? TestAuthenticationDefaults.AuthenticationScheme;
            var authentication = testAuthentication.PreserveApplicationDefaultScheme
                ? services.AddAuthentication()
                : services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = defaultScheme;
                    options.DefaultChallengeScheme = defaultScheme;
                    options.DefaultForbidScheme = defaultScheme;
                    options.DefaultScheme = defaultScheme;
                });
            authentication.AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                TestAuthenticationDefaults.AuthenticationScheme,
                _ => { });
            services.TryAddSingleton<ITestClaimsPrincipalFactory, TestClaimsPrincipalFactory>();

            foreach (var authenticationScheme in testAuthentication.AdditionalSchemes)
            {
                services.AddOptions<TestAuthenticationOptions>(authenticationScheme);
            }

            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddSingleton<IAuthenticationSchemeProvider>(provider =>
                new TestAuthenticationSchemeProvider(
                    provider.GetRequiredService<IOptions<AuthenticationOptions>>(),
                    testAuthentication.AdditionalSchemes));

            foreach (var registration in testAuthentication.EndToEndJwtRegistrations)
            {
                services.Configure<JwtBearerOptions>(
                    registration.AuthenticationScheme,
                    registration.Authority.Configure);
                services.AddSingleton<IStartupFilter>(
                    new TestJwtAuthorityStartupFilter(registration.Authority));
            }

            if (testAuthentication.EndToEndCertificateRegistrations.Count > 0)
            {
                services.AddSingleton<IStartupFilter>(new TestClientCertificateStartupFilter());
            }

            if (testAuthentication.EndToEndJwtRegistrations.Count > 0 ||
                testAuthentication.EndToEndApiKey is not null ||
                testAuthentication.EndToEndCertificateRegistrations.Count > 0)
            {
                services.RemoveAll<IAuthenticationHandlerProvider>();
                services.AddScoped<IAuthenticationHandlerProvider>(provider =>
                    new RecordingAuthenticationHandlerProvider(
                        new AuthenticationHandlerProvider(
                            provider.GetRequiredService<IAuthenticationSchemeProvider>()),
                        testAuthentication.EventRecorder));
            }

            ConfigureServicesForTests(services);
            _configureServices?.Invoke(services);
        });
    }

    /// <summary>Override to add in-memory configuration values to the test application.</summary>
    protected virtual void ConfigureTestConfiguration(IConfigurationBuilder configuration)
    {
    }

    /// <summary>
    /// Adds host settings that are visible while a minimal-hosting application is executing its
    /// top-level startup code. This is appropriate for connection strings and other values read
    /// immediately after <c>WebApplication.CreateBuilder</c>.
    /// </summary>
    protected virtual void ConfigureTestHostSettings(IDictionary<string, string> settings)
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

    /// <summary>Starts a composable arrange, client, and HTTP request scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> Scenario() => new(this);

    /// <summary>Gets a configured local JWT authority, optionally by authentication scheme.</summary>
    public TestJwtAuthority JwtAuthority(string? authenticationScheme = null) =>
        GetAuthenticationConfiguration().GetJwtAuthority(authenticationScheme);

    /// <summary>Gets authentication events captured from real handlers.</summary>
    public TestAuthenticationEventRecorder AuthenticationEvents =>
        GetAuthenticationConfiguration().EventRecorder;

    /// <summary>
    /// Creates an isolated per-test scope. Shared registered resources are protected for the complete
    /// lifetime of the scope and reset before it starts and after it is disposed.
    /// </summary>
    public async Task<TestScenarioScope<TEntryPoint>> CreateTestScenarioScopeAsync(
        Action<TestScenarioScopeBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var scopeBuilder = new TestScenarioScopeBuilder();
        configure?.Invoke(scopeBuilder);

        await _scenarioGate.WaitAsync(cancellationToken);
        TestScenarioScope<TEntryPoint>? scope = null;
        TestScenarioContext? context = null;
        try
        {
            await ResetScenarioResourcesInternalAsync(cancellationToken);
            context = new TestScenarioContext(Guid.NewGuid().ToString("N"));
            var environment = new TestScenarioEnvironmentBuilder();
            ConfigureScenarioEnvironment(environment);
            _configureEnvironment?.Invoke(environment);
            foreach (var definition in scopeBuilder.Environment.Resources)
            {
                environment.AddResource(definition.Name, definition.CreateResource);
            }

            foreach (var definition in environment.Resources)
            {
                var resource = definition.CreateResource(context)
                    ?? throw new InvalidOperationException(
                        $"The test scenario environment resource factory '{definition.Name}' returned null.");
                context.AddEnvironmentResource(definition.Name, resource);
                await resource.StartAsync(cancellationToken);
            }

            var host = WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    foreach (var resource in context.EnvironmentResources)
                    {
                        resource.Resource.ConfigureConfiguration(configuration);
                    }

                    foreach (var action in scopeBuilder.ConfigurationActions)
                    {
                        action(configuration);
                    }
                });
                builder.ConfigureTestServices(services =>
                {
                    foreach (var resource in context.EnvironmentResources)
                    {
                        resource.Resource.ConfigureServices(services);
                    }

                    ConfigureServicesForScenario(services, context);
                    foreach (var action in scopeBuilder.ServiceActions)
                    {
                        action(services);
                    }
                });
            });
            scope = new TestScenarioScope<TEntryPoint>(this, host, context);
            _ = scope.Services;
            await InitializeScenarioAsync(scope, cancellationToken);
            return scope;
        }
        catch
        {
            if (scope is not null)
            {
                try
                {
                    await scope.DisposeAsync();
                }
                catch
                {
                    // Preserve the initialization failure.
                }
            }
            else
            {
                if (context is not null)
                {
                    try
                    {
                        await context.CleanupAsync();
                    }
                    catch
                    {
                        // Preserve the environment startup or host creation failure.
                    }
                }

                _scenarioGate.Release();
            }

            throw;
        }
    }

    /// <summary>
    /// Runs a test inside an isolated scenario scope, captures diagnostics on failure, and always cleans up.
    /// </summary>
    public async Task RunInTestScenarioScopeAsync(
        Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task> test,
        Action<TestScenarioScopeBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);
        await using var scope = await CreateTestScenarioScopeAsync(configure, cancellationToken);
        try
        {
            await test(scope, cancellationToken);
        }
        catch (Exception exception)
        {
            await scope.CaptureFailureAsync(exception, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Runs a test function inside an isolated scenario scope, captures diagnostics on failure,
    /// and always cleans up.
    /// </summary>
    public async Task<TResult> RunInTestScenarioScopeAsync<TResult>(
        Func<TestScenarioScope<TEntryPoint>, CancellationToken, Task<TResult>> test,
        Action<TestScenarioScopeBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);
        await using var scope = await CreateTestScenarioScopeAsync(configure, cancellationToken);
        try
        {
            return await test(scope, cancellationToken);
        }
        catch (Exception exception)
        {
            await scope.CaptureFailureAsync(exception, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Registers mutable state that is automatically reset around every scenario and captured when one fails.
    /// Call this from a derived factory constructor.
    /// </summary>
    protected void RegisterScenarioResource(
        string name,
        Action reset,
        Func<object?> captureDiagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentNullException.ThrowIfNull(captureDiagnostics);

        lock (_scenarioResourcesLock)
        {
            if (_scenarioResources.Any(resource =>
                string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A test scenario resource named '{name}' is already registered.");
            }

            _scenarioResources.Add(new TestScenarioResourceRegistration(
                name,
                _ =>
                {
                    reset();
                    return ValueTask.CompletedTask;
                },
                _ => ValueTask.FromResult(captureDiagnostics())));
        }
    }

    /// <summary>
    /// Registers a resettable resource that participates in every scenario scope.
    /// Call this from a derived factory constructor.
    /// </summary>
    protected void RegisterScenarioResource(string name, ITestScenarioResource resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resource);

        lock (_scenarioResourcesLock)
        {
            if (_scenarioResources.Any(registered =>
                string.Equals(registered.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A test scenario resource named '{name}' is already registered.");
            }

            _scenarioResources.Add(new TestScenarioResourceRegistration(
                name,
                resource.ResetAsync,
                resource.CaptureDiagnosticsAsync));
        }
    }

    /// <summary>Adds or replaces services that exist only for one scenario host.</summary>
    protected virtual void ConfigureServicesForScenario(
        IServiceCollection services,
        TestScenarioContext context)
    {
    }

    /// <summary>Override to add external dependencies that are created for every scenario.</summary>
    protected virtual void ConfigureScenarioEnvironment(TestScenarioEnvironmentBuilder environment)
    {
    }

    /// <summary>Initializes state after the isolated scenario host has started.</summary>
    protected virtual Task InitializeScenarioAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Captures factory-specific state before a failed scenario is cleaned up.</summary>
    protected virtual ValueTask<object?> CaptureScenarioDiagnosticsAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => ValueTask.FromResult<object?>(null);

    /// <summary>Cleans factory-specific state before the isolated scenario host is disposed.</summary>
    protected virtual Task CleanupScenarioAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal string GetAzureAdAuthenticationScheme() => GetAuthenticationConfiguration().AzureAdScheme;

    internal string GetApiKeyAuthenticationScheme() => GetAuthenticationConfiguration().ApiKeyScheme;

    internal TestAuthenticationSchemeBuilder GetAuthenticationConfigurationForClient() =>
        GetAuthenticationConfiguration();

    internal async Task<TestScenarioDiagnostics> CaptureScenarioDiagnosticsInternalAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken)
    {
        var resources = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hostDiagnostics = await CaptureDiagnosticSafelyAsync(
            "Host",
            token => CaptureScenarioDiagnosticsAsync(scope, token),
            cancellationToken);
        if (hostDiagnostics is not null)
        {
            resources["Host"] = hostDiagnostics;
        }

        if (scope.EnvironmentResources.Count > 0)
        {
            var environmentDiagnostics = new Dictionary<string, object?>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var resource in scope.EnvironmentResources)
            {
                environmentDiagnostics[resource.Name] = await CaptureDiagnosticSafelyAsync(
                    resource.Name,
                    resource.Resource.CaptureDiagnosticsAsync,
                    cancellationToken);
            }

            resources["Environment"] = environmentDiagnostics;
        }

        foreach (var resource in GetScenarioResources())
        {
            resources[resource.Name] = await CaptureDiagnosticSafelyAsync(
                resource.Name,
                resource.CaptureDiagnostics,
                cancellationToken);
        }

        return new TestScenarioDiagnostics(
            scope.ScenarioId,
            DateTimeOffset.UtcNow,
            resources);
    }

    internal Task CleanupScenarioInternalAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => CleanupScenarioAsync(scope, cancellationToken);

    internal async Task ResetScenarioResourcesInternalAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var resource in GetScenarioResources())
        {
            try
            {
                await resource.Reset(cancellationToken);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException(
                    $"Failed to reset test scenario resource '{resource.Name}'.",
                    exception));
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more test scenario resources failed to reset.", failures);
        }
    }

    internal void ReleaseScenarioScope() => _scenarioGate.Release();

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
            if (authentication.EndToEndJwtRegistrations.Count > 0 ||
                authentication.EndToEndApiKey is not null ||
                authentication.EndToEndCertificateRegistrations.Count > 0)
            {
                RegisterScenarioResource("Authentication events", authentication.EventRecorder);
            }

            foreach (var registration in authentication.EndToEndJwtRegistrations)
            {
                RegisterScenarioResource(
                    $"JWT authority ({registration.AuthenticationScheme})",
                    registration.Authority);
            }

            return authentication;
        }
    }

    private TestScenarioResourceRegistration[] GetScenarioResources()
    {
        lock (_scenarioResourcesLock)
        {
            return _scenarioResources.ToArray();
        }
    }

    private static async ValueTask<object?> CaptureDiagnosticSafelyAsync(
        string name,
        Func<CancellationToken, ValueTask<object?>> capture,
        CancellationToken cancellationToken)
    {
        try
        {
            return await capture(cancellationToken);
        }
        catch (Exception exception)
        {
            return new TestScenarioDiagnosticCaptureFailure(
                name,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            TestAuthenticationSchemeBuilder.EndToEndJwtRegistration[] registrations;
            lock (_authenticationConfigurationLock)
            {
                registrations = _authenticationConfiguration?.EndToEndJwtRegistrations.ToArray() ?? [];
            }

            foreach (var registration in registrations)
            {
                registration.Authority.Dispose();
            }

            _scenarioGate.Dispose();
        }
    }

    private sealed record TestScenarioResourceRegistration(
        string Name,
        Func<CancellationToken, ValueTask> Reset,
        Func<CancellationToken, ValueTask<object?>> CaptureDiagnostics);

    private sealed record TestScenarioDiagnosticCaptureFailure(
        string Resource,
        string ExceptionType,
        string Message);
}
