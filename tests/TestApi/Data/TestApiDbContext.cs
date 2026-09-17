using Microsoft.EntityFrameworkCore;
using TestApi.Models;

namespace TestApi.Data;

public sealed class TestApiDbContext(DbContextOptions<TestApiDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<CatalogSyncRun> CatalogSyncRuns => Set<CatalogSyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(item => item.Id);
            product.Property(item => item.Name).HasMaxLength(200).IsRequired();
            product.Property(item => item.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CatalogSyncRun>(syncRun =>
        {
            syncRun.HasKey(item => item.Id);
            syncRun.Property(item => item.Category).HasMaxLength(100).IsRequired();
            syncRun.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            syncRun.Property(item => item.FailureReason).HasMaxLength(500);
        });
    }
}
