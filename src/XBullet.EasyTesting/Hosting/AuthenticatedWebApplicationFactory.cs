using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using XBullet.EasyTesting.Authentication;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// An in-memory ASP.NET Core host whose default authentication scheme accepts per-request test users.
/// </summary>
/// <typeparam name="TEntryPoint">
/// The application entry-point type used by <see cref="WebApplicationFactory{TEntryPoint}"/> to
/// locate and bootstrap the ASP.NET Core application.
/// </typeparam>
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
    /// <param name="configureServices">
    /// Configures the test host's service collection after XBullet authentication services are
    /// registered. When <see langword="null"/>, no additional service changes are applied. The
    /// callback runs for each host constructed from this factory, including derived scenario hosts,
    /// and can run concurrently when callers construct derived factories in parallel.
    /// </param>
    /// <param name="configureConfiguration">
    /// Adds or replaces application configuration when the host is constructed. When
    /// <see langword="null"/>, no additional configuration sources are applied. The callback runs
    /// for each constructed host, can run concurrently for parallel derived hosts, and must not
    /// retain or dispose the supplied builder.
    /// </param>
    /// <param name="configureAuthentication">
    /// Configures simulated and end-to-end authentication schemes before the host is constructed.
    /// When <see langword="null"/>, the default simulated test scheme is used. The callback runs
    /// once with a mutable authentication builder.
    /// </param>
    /// <returns>
    /// A new application factory owned by the caller. Dispose it after all clients and scenario
    /// scopes created from it have been disposed.
    /// </returns>
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
    /// <param name="configureHostSettings">
    /// Adds or replaces values in a mutable, case-insensitive settings dictionary before
    /// minimal-hosting startup code runs. The callback runs for each constructed host, can run
    /// concurrently for parallel derived hosts, and must not retain the dictionary.
    /// </param>
    /// <param name="configureServices">
    /// Configures the test host's service collection after XBullet authentication services are
    /// registered. When <see langword="null"/>, no additional service changes are applied. The
    /// callback runs for each constructed host and can run concurrently for parallel derived hosts.
    /// </param>
    /// <param name="configureConfiguration">
    /// Adds or replaces application configuration when the host is constructed. When
    /// <see langword="null"/>, no additional configuration sources are applied. The callback runs
    /// for each constructed host and can run concurrently for parallel derived hosts.
    /// </param>
    /// <param name="configureAuthentication">
    /// Configures simulated and end-to-end authentication schemes. When
    /// <see langword="null"/>, the default simulated test scheme is used.
    /// </param>
    /// <returns>
    /// A new application factory owned by the caller. Dispose it after all clients and scenario
    /// scopes created from it have been disposed.
    /// </returns>
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
            string? originalAuthenticateScheme = null;
            string? originalChallengeScheme = null;
            string? originalForbidScheme = null;
            AuthenticationBuilder authentication;
            if (testAuthentication.HybridDefaultTestScheme is not null)
            {
                authentication = services.AddAuthentication(options =>
                {
                    originalAuthenticateScheme = options.DefaultAuthenticateScheme
                        ?? options.DefaultScheme;
                    originalChallengeScheme = options.DefaultChallengeScheme
                        ?? options.DefaultScheme;
                    originalForbidScheme = options.DefaultForbidScheme
                        ?? options.DefaultScheme;
                    options.DefaultAuthenticateScheme =
                        TestAuthenticationDefaults.HybridAuthenticationScheme;
                    options.DefaultScheme = TestAuthenticationDefaults.HybridAuthenticationScheme;
                });
                authentication.AddPolicyScheme(
                    TestAuthenticationDefaults.HybridAuthenticationScheme,
                    "XBullet hybrid test authentication",
                    options =>
                    {
                        options.ForwardDefaultSelector = context =>
                            context.Request.Headers.ContainsKey(
                                TestAuthenticationDefaults.UserHeaderName)
                                ? testAuthentication.HybridDefaultTestScheme
                                : originalAuthenticateScheme
                                    ?? throw new InvalidOperationException(
                                        "Hybrid test authentication requires the application " +
                                        "to configure a default authentication scheme.");
                        options.ForwardChallenge = originalChallengeScheme;
                        options.ForwardForbid = originalForbidScheme;
                    });
            }
            else if (testAuthentication.PreserveApplicationDefaultScheme)
            {
                authentication = services.AddAuthentication();
            }
            else
            {
                authentication = services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = defaultScheme;
                    options.DefaultChallengeScheme = defaultScheme;
                    options.DefaultForbidScheme = defaultScheme;
                    options.DefaultScheme = defaultScheme;
                });
            }
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
    /// <param name="configuration">
    /// The host-owned configuration builder to mutate during host construction. An override must
    /// not retain or dispose it.
    /// </param>
    protected virtual void ConfigureTestConfiguration(IConfigurationBuilder configuration)
    {
    }

    /// <summary>
    /// Adds host settings that are visible while a minimal-hosting application is executing its
    /// top-level startup code. This is appropriate for connection strings and other values read
    /// immediately after <c>WebApplication.CreateBuilder</c>.
    /// </summary>
    /// <param name="settings">
    /// A mutable, case-insensitive dictionary of host settings. An override may add or replace
    /// values but must not retain the dictionary after this method returns.
    /// </param>
    protected virtual void ConfigureTestHostSettings(IDictionary<string, string> settings)
    {
    }

    /// <summary>Override to replace application services with test doubles.</summary>
    /// <param name="services">
    /// The host-owned service collection to mutate after XBullet authentication services are
    /// registered. An override must not retain or dispose it.
    /// </param>
    protected virtual void ConfigureServicesForTests(IServiceCollection services)
    {
    }

    /// <summary>
    /// Override to map Azure AD, API-key, or custom test profiles to application scheme names.
    /// </summary>
    /// <param name="authentication">
    /// The mutable authentication builder used for this factory. An override may add or replace
    /// scheme mappings but must not retain or dispose the builder.
    /// </param>
    protected virtual void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
    }

    /// <summary>Creates a client that sends requests without a test identity.</summary>
    /// <param name="options">
    /// Client options to apply. When <see langword="null"/>, the factory's default client options
    /// are used. The options object is read but not owned or mutated.
    /// </param>
    /// <returns>
    /// A new anonymous client connected to the in-memory server. The caller owns and must dispose
    /// the client.
    /// </returns>
    public HttpClient CreateAnonymousClient(WebApplicationFactoryClientOptions? options = null) =>
        options is null ? CreateClient() : CreateClient(options);

    /// <summary>Creates a client whose requests use the supplied test identity.</summary>
    /// <param name="user">
    /// The simulated identity sent with requests. When <see langword="null"/>, a default
    /// authenticated test user is created. The supplied user is read but not owned or mutated.
    /// </param>
    /// <param name="options">
    /// Client options to apply. When <see langword="null"/>, the factory's default client options
    /// are used. The options object is read but not owned or mutated.
    /// </param>
    /// <returns>
    /// A new authenticated client connected to the in-memory server. The caller owns and must
    /// dispose the client.
    /// </returns>
    public HttpClient CreateAuthenticatedClient(
        TestUser? user = null,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = CreateAnonymousClient(options);
        return client.AuthenticateAs(user ?? new TestUser());
    }

    /// <summary>Starts a fluent test-client definition. Clients are anonymous until <c>AsUser</c> is called.</summary>
    /// <returns>
    /// A new mutable client builder. Clients produced by the builder are owned by the caller.
    /// </returns>
    public TestClientBuilder<TEntryPoint> Client() => new(this);

    /// <summary>Starts a composable arrange, client, and HTTP request scenario.</summary>
    /// <returns>A new mutable scenario builder associated with this factory.</returns>
    public TestScenarioBuilder<TEntryPoint> Scenario() => new(this);

    /// <summary>Gets a configured local JWT authority, optionally by authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The exact registered authentication scheme to select. When <see langword="null"/>, the
    /// default local JWT authority is returned.
    /// </param>
    /// <returns>The factory-owned authority for the selected scheme. Do not dispose it.</returns>
    public TestJwtAuthority JwtAuthority(string? authenticationScheme = null) =>
        GetAuthenticationConfiguration().GetJwtAuthority(authenticationScheme);

    /// <summary>Gets authentication events captured from real handlers.</summary>
    /// <value>
    /// The factory-owned recorder shared with configured end-to-end authentication handlers. Do
    /// not dispose it. Scenario creation resets its mutable contents before and after each scope.
    /// </value>
    public TestAuthenticationEventRecorder AuthenticationEvents =>
        GetAuthenticationConfiguration().EventRecorder;

    /// <summary>
    /// Creates an isolated per-test scope. Shared registered resources are protected for the complete
    /// lifetime of the scope and reset before it starts and after it is disposed.
    /// </summary>
    /// <param name="configure">
    /// Configures service, application-configuration, and environment-resource overrides for this
    /// scope. When <see langword="null"/>, only factory-level configuration is used. The callback
    /// runs once, synchronously, before the method waits for another scope to finish, so concurrent
    /// scope-creation calls can invoke it concurrently.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels waiting for exclusive access, shared-resource reset, environment-resource startup,
    /// host creation, or scenario initialization. Resources created before cancellation are still
    /// cleaned up. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A caller-owned scope containing the isolated host and scenario resources. The caller must
    /// asynchronously dispose the scope to release the factory for the next scenario.
    /// </returns>
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
    /// <param name="test">
    /// The asynchronous test callback invoked once with the newly created scope and
    /// <paramref name="cancellationToken"/>. A single factory never runs two test callbacks
    /// concurrently.
    /// </param>
    /// <param name="configure">
    /// Configures overrides for the new scope. When <see langword="null"/>, only factory-level
    /// configuration is used. The callback runs once before scope creation begins and can run
    /// concurrently across simultaneous calls.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels scope creation and the test callback. Failure diagnostics and cleanup run with a
    /// non-cancelable token so cancellation cannot leave factory-owned state uncleared. The default
    /// token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the test callback and all scope cleanup complete.</returns>
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
    /// <typeparam name="TResult">The value returned by the asynchronous test callback.</typeparam>
    /// <param name="test">
    /// The asynchronous test callback invoked once with the newly created scope and
    /// <paramref name="cancellationToken"/>. A single factory never runs two test callbacks
    /// concurrently.
    /// </param>
    /// <param name="configure">
    /// Configures overrides for the new scope. When <see langword="null"/>, only factory-level
    /// configuration is used. The callback runs once before scope creation begins and can run
    /// concurrently across simultaneous calls.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels scope creation and the test callback. Failure diagnostics and cleanup run with a
    /// non-cancelable token so cancellation cannot leave factory-owned state uncleared. The default
    /// token does not request cancellation.
    /// </param>
    /// <returns>
    /// The value produced by <paramref name="test"/> after the scope has been cleaned up.
    /// </returns>
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
    /// <param name="name">
    /// The non-empty, case-insensitively unique name used as the diagnostic-data key.
    /// </param>
    /// <param name="reset">
    /// The callback invoked before and after every scenario to clear shared mutable state. Calls are
    /// sequential and never concurrent for a single factory. The factory does not own captured
    /// objects referenced by the callback.
    /// </param>
    /// <param name="captureDiagnostics">
    /// The callback invoked once when a scenario fails, before cleanup and reset. It returns a
    /// serializable diagnostic value or <see langword="null"/>. Exceptions are recorded as
    /// diagnostic-capture failures instead of replacing the original test failure.
    /// </param>
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
    /// <param name="name">
    /// The non-empty, case-insensitively unique name used as the diagnostic-data key.
    /// </param>
    /// <param name="resource">
    /// The resource whose reset and diagnostic callbacks participate in every scenario. The
    /// factory does not take ownership or dispose the resource.
    /// </param>
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
    /// <param name="services">
    /// The scenario host's service collection to mutate after environment resources have
    /// contributed services. An override must not retain or dispose it.
    /// </param>
    /// <param name="context">
    /// The scenario context used to register scenario-owned cleanup and inspect the stable scenario
    /// identifier. The factory retains ownership of the context.
    /// </param>
    protected virtual void ConfigureServicesForScenario(
        IServiceCollection services,
        TestScenarioContext context)
    {
    }

    /// <summary>Override to add external dependencies that are created for every scenario.</summary>
    /// <param name="environment">
    /// The mutable builder receiving environment-resource factories. The callback runs once per
    /// scenario before any resource factory is invoked. The factory owns the builder.
    /// </param>
    protected virtual void ConfigureScenarioEnvironment(TestScenarioEnvironmentBuilder environment)
    {
    }

    /// <summary>Initializes state after the isolated scenario host has started.</summary>
    /// <param name="scope">
    /// The newly started, factory-owned scenario scope. An override may use it but must not dispose
    /// it.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels scenario initialization. When cancellation or another failure occurs, the scope is
    /// still cleaned up. The token is the one supplied to scope creation.
    /// </param>
    /// <returns>A task that completes when scenario-specific initialization has finished.</returns>
    protected virtual Task InitializeScenarioAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Captures factory-specific state before a failed scenario is cleaned up.</summary>
    /// <param name="scope">
    /// The failed, factory-owned scenario scope. An override may inspect it but must not dispose it.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels diagnostic capture. The built-in failure path passes a non-cancelable token so
    /// diagnostics can be attempted even when the test token was canceled.
    /// </param>
    /// <returns>
    /// A serializable diagnostic value, or <see langword="null"/> when the factory has no additional
    /// state. Exceptions are converted to a diagnostic-capture failure record.
    /// </returns>
    protected virtual ValueTask<object?> CaptureScenarioDiagnosticsAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) => ValueTask.FromResult<object?>(null);

    /// <summary>Cleans factory-specific state before the isolated scenario host is disposed.</summary>
    /// <param name="scope">
    /// The factory-owned scenario scope being cleaned up. An override may inspect it but must not
    /// dispose it.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels factory-specific cleanup when a caller supplies a cancelable token. Normal scope
    /// disposal passes a non-cancelable token so remaining cleanup operations can still run.
    /// </param>
    /// <returns>A task that completes when factory-specific cleanup has finished.</returns>
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
