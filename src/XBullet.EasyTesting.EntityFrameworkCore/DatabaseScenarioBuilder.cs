using Microsoft.EntityFrameworkCore;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>Fluently arranges one scoped EF Core database scenario.</summary>
public sealed class DatabaseScenarioBuilder<TEntryPoint, TDbContext>
    where TEntryPoint : class
    where TDbContext : DbContext
{
    private readonly EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext> _factory;
    private readonly List<Func<TDbContext, CancellationToken, Task>> _actions = [];
    private bool _initialize;
    private bool _recreate;
    private bool _transactional;
    private bool _executed;

    internal DatabaseScenarioBuilder(
        EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>Ensures that the database schema exists before running subsequent actions.</summary>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> EnsureCreated()
    {
        _initialize = true;
        _recreate = false;
        return this;
    }

    /// <summary>Deletes and recreates the isolated test database before subsequent actions.</summary>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Recreate()
    {
        _recreate = true;
        _initialize = false;
        return this;
    }

    /// <summary>Adds entities during scenario execution.</summary>
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
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Apply(
        Func<TDbContext, CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
        return this;
    }

    /// <summary>Runs the arranged actions in a transaction supported by the configured provider.</summary>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> InTransaction()
    {
        _transactional = true;
        return this;
    }

    /// <summary>Executes the arranged operations once and saves tracked changes.</summary>
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_executed)
        {
            throw new InvalidOperationException("A database scenario can only be executed once.");
        }

        _executed = true;
        return _factory.WithDbContextAsync(ExecuteCoreAsync, cancellationToken);
    }

    private async Task ExecuteCoreAsync(TDbContext database, CancellationToken cancellationToken)
    {
        if (_recreate)
        {
            await database.Database.EnsureDeletedAsync(cancellationToken);
            await database.Database.EnsureCreatedAsync(cancellationToken);
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
