using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>
/// An authenticated application factory with scoped, serialized actions for an EF Core test database.
/// </summary>
/// <typeparam name="TEntryPoint">The application entry-point type hosted by the test factory.</typeparam>
/// <typeparam name="TDbContext">The EF Core context type replaced and resolved for database operations.</typeparam>
/// <remarks>
/// Operations against the factory database are serialized and use a fresh factory-owned dependency-
/// injection scope and context. Scenario operations use the specified scenario's isolated services.
/// Callbacks must not retain or dispose contexts supplied by this factory.
/// </remarks>
public abstract class EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>
    : AuthenticatedWebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
    where TDbContext : DbContext
{
    private readonly SemaphoreSlim _databaseGate = new(1, 1);

    /// <summary>
    /// Replaces the application's concrete context registration and delegates provider configuration
    /// to <see cref="ConfigureDatabaseServices"/>.
    /// </summary>
    /// <param name="services">
    /// The mutable test-host service collection. Existing context, context-factory, and options
    /// registrations for <typeparamref name="TDbContext"/> are removed before replacements are added.
    /// </param>
    protected sealed override void ConfigureServicesForTests(IServiceCollection services)
    {
        RemoveDatabaseServices(services);
        ConfigureDatabaseServices(services);
        ConfigureExpectedDatabaseWarnings(services);
        ConfigureAdditionalServicesForTests(services);
    }

    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> with the chosen test provider and connection.
    /// </summary>
    /// <param name="services">
    /// The mutable test-host service collection to update in place. The host owns the collection and
    /// all registered services.
    /// </param>
    protected abstract void ConfigureDatabaseServices(IServiceCollection services);

    /// <summary>Allows derived factories to replace services unrelated to the database.</summary>
    /// <param name="services">
    /// The mutable factory-host service collection to update in place after database registration.
    /// The host owns the collection and all registered services.
    /// </param>
    protected virtual void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
    }

    /// <summary>
    /// Registers the database used by one scenario. Override this to allocate a distinct database,
    /// schema, or connection and register its cleanup through <paramref name="context"/>.
    /// </summary>
    /// <param name="services">
    /// The mutable scenario-host service collection to update in place. Existing registrations for
    /// <typeparamref name="TDbContext"/> have already been removed.
    /// </param>
    /// <param name="context">
    /// The scenario configuration context. Use it to identify the scenario and register resources
    /// whose ownership is transferred to the scenario for cleanup; do not retain the context.
    /// </param>
    protected virtual void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        TestScenarioContext context) => ConfigureDatabaseServices(services);

    /// <summary>Allows derived factories to add services that exist only in scenario hosts.</summary>
    /// <param name="services">
    /// The mutable scenario-host service collection to update in place after database registration.
    /// </param>
    /// <param name="context">
    /// The scenario configuration context. It may be used to register scenario-owned resources and
    /// must not be retained after configuration.
    /// </param>
    protected virtual void ConfigureAdditionalServicesForScenario(
        IServiceCollection services,
        TestScenarioContext context)
    {
    }

    /// <summary>Starts a fluent database scenario definition.</summary>
    /// <returns>A new mutable, single-use builder targeting the factory database.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Database() => new(this);

    /// <summary>Starts a fluent database scenario against a test scenario's isolated database.</summary>
    /// <param name="scope">
    /// The non-null, active scenario scope whose service provider supplies the context. The caller
    /// retains ownership of the scope.
    /// </param>
    /// <returns>A new mutable, single-use builder targeting the scope's isolated database.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Database(
        TestScenarioScope<TEntryPoint> scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new DatabaseScenarioBuilder<TEntryPoint, TDbContext>(this, scope);
    }

    /// <summary>Creates the test database schema if it does not already exist.</summary>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and schema creation. The default token does
    /// not request cancellation.
    /// </param>
    /// <returns>A task that completes after EF Core has ensured the factory database exists.</returns>
    public Task InitializeDatabaseAsync(CancellationToken cancellationToken = default) =>
        WithDbContextAsync(
            async (database, token) =>
            {
                await database.Database.EnsureCreatedAsync(token);
            },
            cancellationToken);

    /// <summary>
    /// Deletes and recreates the complete test database. Only use this with an isolated test database.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access, deletion, or creation. The default token
    /// does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the factory database has been deleted and recreated.</returns>
    public Task RecreateDatabaseAsync(CancellationToken cancellationToken = default) =>
        WithDbContextAsync(
            async (database, token) =>
            {
                await database.Database.EnsureDeletedAsync(token);
                await database.Database.EnsureCreatedAsync(token);
            },
            cancellationToken);

    /// <summary>Executes a scoped database action and persists its tracked changes.</summary>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a fresh factory-owned context and the
    /// supplied token. It must not retain or dispose the context. The factory saves tracked changes
    /// after the callback succeeds.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and is passed to the callback and save.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the callback, save, and scoped-context disposal.</returns>
    public Task ExecuteDatabaseAsync(
        Func<TDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return WithDbContextAsync(
            async (database, token) =>
            {
                await action(database, token);
                await database.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    /// <summary>Executes a read operation in a fresh dependency-injection scope.</summary>
    /// <typeparam name="TResult">The materialized result type returned by the query.</typeparam>
    /// <param name="query">
    /// A non-null asynchronous callback invoked once with a fresh factory-owned context and the
    /// supplied token. It must fully materialize its result and must not retain or dispose the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and is passed to the query. The default
    /// token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result is the materialized value produced by <paramref name="query"/>. The caller
    /// owns the value; it must not require the disposed context for later enumeration or loading.
    /// </returns>
    public Task<TResult> QueryDatabaseAsync<TResult>(
        Func<TDbContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return WithDbContextAsync(query, cancellationToken);
    }

    /// <summary>Executes an action and saves changes in a scenario's isolated database.</summary>
    /// <param name="scope">
    /// The non-null, active scenario scope whose service provider supplies the context. The caller
    /// retains ownership of the scope.
    /// </param>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a scenario-owned scoped context and the
    /// supplied token. It must not retain or dispose the context. Tracked changes are saved after it succeeds.
    /// </param>
    /// <param name="cancellationToken">
    /// A token passed to the callback and save operation. The default token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the callback, save, and scoped-context disposal.</returns>
    public Task ExecuteDatabaseAsync(
        TestScenarioScope<TEntryPoint> scope,
        Func<TDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return WithScenarioDbContextAsync(
            scope,
            async (database, token) =>
            {
                await action(database, token);
                await database.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    /// <summary>Executes a query in a scenario's isolated database.</summary>
    /// <typeparam name="TResult">The materialized result type returned by the query.</typeparam>
    /// <param name="scope">
    /// The non-null, active scenario scope whose service provider supplies the context. The caller
    /// retains ownership of the scope.
    /// </param>
    /// <param name="query">
    /// A non-null asynchronous callback invoked once with a scenario-owned scoped context and the
    /// supplied token. It must fully materialize its result and must not retain or dispose the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A token passed to the query. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result is the materialized value produced by <paramref name="query"/>. The caller
    /// owns the value; it must not depend on the disposed context.
    /// </returns>
    public Task<TResult> QueryDatabaseAsync<TResult>(
        TestScenarioScope<TEntryPoint> scope,
        Func<TDbContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return WithScenarioDbContextAsync(scope, query, cancellationToken);
    }

    /// <summary>Executes a scoped database action against a scenario's isolated context.</summary>
    /// <param name="scope">
    /// The non-null, active scenario scope whose service provider supplies the context. The caller
    /// retains ownership of the scope.
    /// </param>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a scenario-owned scoped context and the
    /// supplied token. It must not retain or dispose the context. Changes are not saved automatically.
    /// </param>
    /// <param name="cancellationToken">
    /// A token passed unchanged to the callback. The default token does not request cancellation;
    /// the callback decides which operations observe it.
    /// </param>
    /// <returns>A task that completes after the callback and scoped-context disposal.</returns>
    public async Task WithScenarioDbContextAsync(
        TestScenarioScope<TEntryPoint> scope,
        Func<TDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(action);
        await using var serviceScope = scope.Services.CreateAsyncScope();
        var database = serviceScope.ServiceProvider.GetRequiredService<TDbContext>();
        await action(database, cancellationToken);
    }

    /// <summary>Executes a scoped database query against a scenario's isolated context.</summary>
    /// <typeparam name="TResult">The materialized result type returned by the callback.</typeparam>
    /// <param name="scope">
    /// The non-null, active scenario scope whose service provider supplies the context. The caller
    /// retains ownership of the scope.
    /// </param>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a scenario-owned scoped context and the
    /// supplied token. It must fully materialize its result and must not retain or dispose the context.
    /// Changes are not saved automatically.
    /// </param>
    /// <param name="cancellationToken">
    /// A token passed unchanged to the callback. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result is the materialized value produced by <paramref name="action"/>. The caller
    /// owns the value; it must not depend on the disposed context.
    /// </returns>
    public async Task<TResult> WithScenarioDbContextAsync<TResult>(
        TestScenarioScope<TEntryPoint> scope,
        Func<TDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(action);
        await using var serviceScope = scope.Services.CreateAsyncScope();
        var database = serviceScope.ServiceProvider.GetRequiredService<TDbContext>();
        return await action(database, cancellationToken);
    }

    /// <summary>Adds entities to the test database and persists them.</summary>
    /// <typeparam name="TEntity">The mapped reference-entity type to add.</typeparam>
    /// <param name="entities">
    /// A non-null sequence of entities accepted by EF Core. The sequence is enumerated immediately
    /// and its elements are retained until the database operation completes.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access, adding entities, or saving changes. The
    /// default token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after all entities have been added and tracked changes saved.</returns>
    public Task SeedDatabaseAsync<TEntity>(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        var materializedEntities = entities.ToArray();

        return ExecuteDatabaseAsync(
            async (database, token) =>
            {
                await database.Set<TEntity>().AddRangeAsync(materializedEntities, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Executes an action in a database transaction, saves tracked changes, and commits on success.
    /// </summary>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once inside the transaction with a fresh factory-owned
    /// context and the supplied token. It must not retain or dispose the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and is passed to transaction, callback,
    /// save, and commit operations. The default token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the transaction commits and owned resources are disposed.</returns>
    /// <remarks>The configured provider must support transactions. Failure before commit causes rollback on disposal.</remarks>
    public Task ExecuteInTransactionAsync(
        Func<TDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return WithDbContextAsync(
            async (database, token) =>
            {
                await using var transaction = await database.Database.BeginTransactionAsync(token);
                await action(database, token);
                await database.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs an action against a fresh scoped context without automatically saving tracked changes.
    /// </summary>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a fresh factory-owned context and the
    /// supplied token. It must not retain or dispose the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and is passed unchanged to the callback.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>A task that completes after the callback and scoped-context disposal.</returns>
    public async Task WithDbContextAsync(
        Func<TDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _databaseGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TDbContext>();
            await action(database, cancellationToken);
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    /// <summary>
    /// Runs a function against a fresh scoped context without automatically saving tracked changes.
    /// </summary>
    /// <typeparam name="TResult">The materialized result type returned by the callback.</typeparam>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once with a fresh factory-owned context and the
    /// supplied token. It must fully materialize its result and must not retain or dispose the context.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for serialized access and is passed unchanged to the callback.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result is the materialized value produced by <paramref name="action"/>. The caller
    /// owns the value; it must not depend on the disposed context.
    /// </returns>
    public async Task<TResult> WithDbContextAsync<TResult>(
        Func<TDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _databaseGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TDbContext>();
            return await action(database, cancellationToken);
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    /// <inheritdoc />
    protected sealed override void ConfigureServicesForScenario(
        IServiceCollection services,
        TestScenarioContext context)
    {
        RemoveDatabaseServices(services);
        ConfigureScenarioDatabaseServices(services, context);
        ConfigureExpectedDatabaseWarnings(services);
        ConfigureAdditionalServicesForScenario(services, context);
    }

    internal static void ConfigureExpectedDatabaseWarnings(IServiceCollection services)
    {
        static void Configure(DbContextOptionsBuilder options) =>
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

#if NET9_0_OR_GREATER
        services.ConfigureDbContext<TDbContext>(Configure);
#else
        // ConfigureDbContext was introduced in EF Core 9. Decorate EF Core 8's options factory so
        // this policy runs after every configuration supplied by the derived test factory.
        var optionsServiceType = typeof(DbContextOptions<TDbContext>);
        var optionsRegistrationIndex = -1;
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (!services[index].IsKeyedService &&
                services[index].ServiceType == optionsServiceType)
            {
                optionsRegistrationIndex = index;
                break;
            }
        }

        if (optionsRegistrationIndex < 0)
        {
            services.AddDbContext<TDbContext>(Configure);
            return;
        }

        var optionsRegistration = services[optionsRegistrationIndex];
        services[optionsRegistrationIndex] = ServiceDescriptor.Describe(
            optionsServiceType,
            provider =>
            {
                var options = optionsRegistration.ImplementationInstance
                    ?? optionsRegistration.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.GetServiceOrCreateInstance(
                        provider,
                        optionsRegistration.ImplementationType!);
                var builder = new DbContextOptionsBuilder<TDbContext>(
                    (DbContextOptions<TDbContext>)options);
                Configure(builder);
                return builder.Options;
            },
            optionsRegistration.Lifetime);
#endif
    }

    private static void RemoveDatabaseServices(IServiceCollection services)
    {
        services.RemoveAll<TDbContext>();
        services.RemoveAll<IDbContextFactory<TDbContext>>();
        services.RemoveAll<DbContextOptions<TDbContext>>();
#if NET9_0_OR_GREATER
        services.RemoveAll<IDbContextOptionsConfiguration<TDbContext>>();
#endif
    }

    /// <inheritdoc />
    protected override Task InitializeScenarioAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) =>
        WithScenarioDbContextAsync(
            scope,
            InitializeScenarioDatabaseAsync,
            cancellationToken);

    /// <summary>
    /// Initializes a scenario database. Override this to run application migrations, invoke a
    /// schema verifier, restore a template, or apply another application-specific lifecycle.
    /// </summary>
    /// <param name="database">
    /// The scenario-owned scoped context. Use it only for this callback and do not retain or dispose it.
    /// </param>
    /// <param name="cancellationToken">A token that cancels initialization operations.</param>
    /// <returns>
    /// A task that completes after initialization. The default implementation deletes and recreates
    /// the complete isolated scenario database.
    /// </returns>
    protected virtual async Task InitializeScenarioDatabaseAsync(
        TDbContext database,
        CancellationToken cancellationToken)
    {
        await database.Database.EnsureDeletedAsync(cancellationToken);
        await database.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> CaptureScenarioDiagnosticsAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) =>
        await WithScenarioDbContextAsync<object?>(
            scope,
            (database, _) => Task.FromResult<object?>(new
            {
                database.Database.ProviderName,
                Entities = database.Model.GetEntityTypes()
                    .Select(entity => entity.ClrType.FullName ?? entity.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()
            }),
            cancellationToken);

    /// <inheritdoc />
    protected override Task CleanupScenarioAsync(
        TestScenarioScope<TEntryPoint> scope,
        CancellationToken cancellationToken) =>
        WithScenarioDbContextAsync(
            scope,
            CleanupScenarioDatabaseAsync,
            cancellationToken);

    /// <summary>
    /// Cleans up a scenario database. The default implementation retries SQLite file cleanup and
    /// attaches <see cref="SqliteDatabaseCleanupDiagnostics"/> to terminal cleanup failures.
    /// Override this when the application owns database disposal or cleanup.
    /// </summary>
    /// <param name="database">
    /// The scenario-owned scoped context. Use it only for this callback and do not retain or dispose it.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels deletion and transient SQLite retry delays.
    /// </param>
    /// <returns>
    /// A task that completes after cleanup. The default deletes the database; for SQLite it clears
    /// connection pools and retries transient file-lock failures before attaching terminal diagnostics.
    /// </returns>
    protected virtual Task CleanupScenarioDatabaseAsync(
        TDbContext database,
        CancellationToken cancellationToken) =>
        DatabaseCleanup.EnsureDeletedAsync(database, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _databaseGate.Dispose();
        }
    }
}
