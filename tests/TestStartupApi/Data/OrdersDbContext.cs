using Microsoft.EntityFrameworkCore;
using TestStartupApi.Models;

namespace TestStartupApi.Data;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<ImportedOrder> Orders => Set<ImportedOrder>();

    public DbSet<ImportedOrderItem> OrderItems => Set<ImportedOrderItem>();

    public DbSet<OrderShipment> OrderShipments => Set<OrderShipment>();

    public DbSet<ShipmentDetail> ShipmentDetails => Set<ShipmentDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportedOrder>(order =>
        {
            order.HasKey(item => item.Id);
            order.HasIndex(item => item.ExternalId).IsUnique();
            order.Property(item => item.ExternalId).HasMaxLength(100).IsRequired();
            order.Property(item => item.CustomerId).HasMaxLength(100).IsRequired();
            order.Property(item => item.Description).HasMaxLength(500).IsRequired();
            order.Property(item => item.Total).HasPrecision(18, 2);
            order.Property(item => item.ImportedBy).HasMaxLength(100).IsRequired();
            order.HasMany(item => item.Items)
                .WithOne(item => item.Order)
                .HasForeignKey(item => item.ImportedOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            order.HasMany(item => item.Shipments)
                .WithOne(item => item.Order)
                .HasForeignKey(item => item.ImportedOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImportedOrderItem>(item =>
        {
            item.HasKey(value => value.Id);
            item.HasIndex(value => new { value.ImportedOrderId, value.Sku }).IsUnique();
            item.Property(value => value.Sku).HasMaxLength(100).IsRequired();
            item.Property(value => value.UnitPrice).HasPrecision(18, 2);
            item.HasMany(value => value.ShipmentDetails)
                .WithOne(value => value.OrderItem)
                .HasForeignKey(value => value.ImportedOrderItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderShipment>(shipment =>
        {
            shipment.HasKey(value => value.Id);
            shipment.HasIndex(value => value.ProviderShipmentId).IsUnique();
            shipment.Property(value => value.ProviderShipmentId).HasMaxLength(100).IsRequired();
            shipment.Property(value => value.Carrier).HasMaxLength(100).IsRequired();
            shipment.Property(value => value.TrackingNumber).HasMaxLength(200).IsRequired();
            shipment.Property(value => value.Status).HasConversion<string>().HasMaxLength(20);
            shipment.HasMany(value => value.Details)
                .WithOne(value => value.Shipment)
                .HasForeignKey(value => value.OrderShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShipmentDetail>(detail =>
        {
            detail.HasKey(value => value.Id);
            detail.HasIndex(value => new
            {
                value.OrderShipmentId,
                value.ImportedOrderItemId
            }).IsUnique();
        });
    }
}
