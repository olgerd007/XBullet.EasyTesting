using XBullet.EasyTesting.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>
/// An authenticated application factory with scoped, serialized actions for an EF Core test database.
/// </summary>
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
    protected sealed override void ConfigureServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<TDbContext>();
        services.RemoveAll<DbContextOptions<TDbContext>>();
        ConfigureDatabaseServices(services);
        ConfigureAdditionalServicesForTests(services);
    }

    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> with the chosen test provider and connection.
    /// </summary>
    protected abstract void ConfigureDatabaseServices(IServiceCollection services);

    /// <summary>Allows derived factories to replace services unrelated to the database.</summary>
    protected virtual void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
    }

    /// <summary>Starts a fluent database scenario definition.</summary>
    public DatabaseScenarioBuilder<TEntryPoint, TDbContext> Database() => new(this);

    /// <summary>Creates the test database schema if it does not already exist.</summary>
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
    public Task RecreateDatabaseAsync(CancellationToken cancellationToken = default) =>
        WithDbContextAsync(
            async (database, token) =>
            {
                await database.Database.EnsureDeletedAsync(token);
                await database.Database.EnsureCreatedAsync(token);
            },
            cancellationToken);

    /// <summary>Executes a scoped database action and persists its tracked changes.</summary>
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
    public Task<TResult> QueryDatabaseAsync<TResult>(
        Func<TDbContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return WithDbContextAsync(query, cancellationToken);
    }

    /// <summary>Adds entities to the test database and persists them.</summary>
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
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _databaseGate.Dispose();
        }
    }
}
