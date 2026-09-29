using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>
/// An Entity Framework Core test factory backed by the EF Core in-memory provider.
/// </summary>
/// <remarks>
/// Each factory and each test scenario receives an isolated in-memory database.
/// The in-memory provider is not relational and does not support transactions.
/// Use <see cref="EntityFrameworkWebApplicationFactory{TEntryPoint, TDbContext}"/>
/// with a relational provider when relational behavior is part of the test.
/// </remarks>
/// <typeparam name="TEntryPoint">The application entry-point type hosted by the test factory.</typeparam>
/// <typeparam name="TDbContext">The EF Core context type registered with the in-memory provider.</typeparam>
public class InMemoryEntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>
    : EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>
    where TEntryPoint : class
    where TDbContext : DbContext
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly string _databaseName = $"XBullet.EasyTesting-{Guid.NewGuid():N}";

    /// <inheritdoc />
    protected sealed override void ConfigureDatabaseServices(IServiceCollection services) =>
        AddDbContext(services, _databaseName);

    /// <inheritdoc />
    protected sealed override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        TestScenarioContext context) =>
        AddDbContext(services, $"{_databaseName}-{context.ScenarioId}");

    /// <summary>
    /// Configures options shared by the factory database and every scenario database.
    /// </summary>
    /// <param name="options">
    /// The mutable options builder to configure in place. The factory owns it; implementations must
    /// not retain it. The in-memory provider and isolated database name have already been configured.
    /// </param>
    /// <remarks>This callback may run more than once as dependency-injection providers build context options.</remarks>
    protected virtual void ConfigureInMemoryDatabase(DbContextOptionsBuilder options)
    {
    }

    private void AddDbContext(IServiceCollection services, string databaseName)
    {
        services.AddDbContext<TDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName, _databaseRoot);
            ConfigureInMemoryDatabase(options);
        });
    }
}
