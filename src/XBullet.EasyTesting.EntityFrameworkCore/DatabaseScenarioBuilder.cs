using Microsoft.EntityFrameworkCore;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>Fluently arranges one scoped EF Core database scenario.</summary>
/// <typeparam name="TEntryPoint">The application entry-point type hosted by the test factory.</typeparam>
/// <typeparam name="TDbContext">The EF Core context type used by the application.</typeparam>
/// <remarks>
/// This mutable builder is not thread-safe and can execute only once. Arranged callbacks execute
/// sequentially against one factory-owned scoped context and must not retain or dispose it.
/// </remarks>
public sealed class DatabaseScenarioBuilder<TEntryPoint, TDbContext>
    where TEntryPoint : class
    where TDbContext : DbContext
{
    private readonly EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext> _factory;
    private readonly TestScenarioScope<TEntryPoint>? _scope;
    private readonly List<Func<TDbContext, CancellationToken, Task>> _actions = [];
    private bool _initialize;
    private bool _recreate;
    private bool _transactional;
    private bool _executed;
    private Func<TDbContext, CancellationToken, Task>? _recreateDatabase;

    internal DatabaseScenarioBuilder(
        EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext> factory,
        TestScenarioScope<TEntryPoint>? scope = null)
    {
        _factory = factory;
        _scope = scope;
    }

    /// <summary>Ensures that the database schema exists before running subsequent actions.</summary>
    /// <returns>This builder, for chaining.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> EnsureCreated()
    {
        _initialize = true;
        _recreate = false;
        return this;
    }

    /// <summary>Deletes and recreates the isolated test database before subsequent actions.</summary>
    /// <returns>This builder, for chaining.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Recreate()
    {
        _recreate = true;
        _initialize = false;
        return this;
    }

    /// <summary>
    /// Uses an application-specific database recreation operation before subsequent actions.
    /// </summary>
    /// <param name="recreateDatabase">
    /// A non-null asynchronous callback invoked once before arranged actions. It receives the
    /// factory-owned scoped context and execution token and must not retain or dispose the context.
    /// The callback is responsible for deleting, migrating, restoring, or otherwise recreating the
    /// database; changes are saved after all arranged actions, not immediately after this callback.
    /// </param>
    /// <returns>This builder, for chaining. The callback is retained until execution.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> RecreateDatabaseWith(
        Func<TDbContext, CancellationToken, Task> recreateDatabase)
    {
        ArgumentNullException.ThrowIfNull(recreateDatabase);
        _recreateDatabase = recreateDatabase;
        _recreate = true;
        _initialize = false;
        return this;
    }

    /// <summary>Adds entities during scenario execution.</summary>
    /// <typeparam name="TEntity">The mapped reference-entity type to add.</typeparam>
    /// <param name="entities">
    /// The non-null array of entities to add. Its contents are copied when this method is called;
    /// each element must be a valid entity accepted by EF Core.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Seed<TEntity>(params TEntity[] entities)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        var materializedEntities = entities.ToArray();
        _actions.Add((database, token) =>
            database.Set<TEntity>().AddRangeAsync(materializedEntities, token));
        return this;
    }

    /// <summary>Adds a synchronous context mutation to the scenario.</summary>
    /// <param name="action">
    /// A non-null callback invoked once in arrangement order with the factory-owned scoped context.
    /// It must not retain or dispose the context. Tracked changes are saved after all actions finish.
    /// </param>
    /// <returns>This builder, for chaining. The callback is retained until execution.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Apply(Action<TDbContext> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add((database, _) =>
        {
            action(database);
            return Task.CompletedTask;
        });
        return this;
    }

    /// <summary>Adds an asynchronous context mutation to the scenario.</summary>
    /// <param name="action">
    /// A non-null asynchronous callback invoked once in arrangement order with the factory-owned
    /// scoped context and execution token. It must not retain or dispose the context. Tracked changes
    /// are saved after all actions finish.
    /// </param>
    /// <returns>This builder, for chaining. The callback is retained until execution.</returns>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Apply(
        Func<TDbContext, CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    /// <summary>Runs the arranged actions in a transaction supported by the configured provider.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// The configured provider must support transactions. The transaction commits after all actions
    /// and the final save succeed; disposal rolls it back when execution fails before commit.
    /// </remarks>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> InTransaction()
    {
        _transactional = true;
        return this;
    }

    /// <summary>Executes the arranged operations once and saves tracked changes.</summary>
    /// <param name="cancellationToken">
    /// A token passed to database operations and asynchronous callbacks. The default token does not
    /// request cancellation.
    /// </param>
    /// <returns>
    /// A task that completes after optional initialization or recreation, all actions, the final
    /// save, and any requested transaction commit. A second call throws an exception.
    /// </returns>
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_executed)
        {
            throw new InvalidOperationException("A database scenario can only be executed once.");
        }

        _executed = true;
        return _scope is null
            ? _factory.WithDbContextAsync(ExecuteCoreAsync, cancellationToken)
            : _factory.WithScenarioDbContextAsync(_scope, ExecuteCoreAsync, cancellationToken);
    }

    private async Task ExecuteCoreAsync(TDbContext database, CancellationToken cancellationToken)
    {
        if (_recreate)
        {
            if (_recreateDatabase is not null)
            {
                await _recreateDatabase(database, cancellationToken);
            }
            else
            {
                await database.Database.EnsureDeletedAsync(cancellationToken);
                await database.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
        else if (_initialize)
        {
            await database.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (_transactional)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await RunActionsAndSaveAsync(database, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await RunActionsAndSaveAsync(database, cancellationToken);
    }

    private async Task RunActionsAndSaveAsync(
        TDbContext database,
        CancellationToken cancellationToken)
    {
        foreach (var action in _actions)
        {
            await action(database, cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
