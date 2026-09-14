using Microsoft.EntityFrameworkCore;
using TestApi.Models;

namespace TestApi.Data;

public sealed class TestApiDbContext(DbContextOptions<TestApiDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(item => item.Id);
            product.Property(item => item.Name).HasMaxLength(200).IsRequired();
            product.Property(item => item.Price).HasPrecision(18, 2);
        });
    }
}
