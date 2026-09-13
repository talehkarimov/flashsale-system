using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;
using FlashSale.Infrastructure.Outbox.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

public sealed class SaleDbContext(DbContextOptions<SaleDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FlashSale.Domain.Inventory.Inventory> Inventory => Set<FlashSale.Domain.Inventory.Inventory>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderPaymentOutboxMessage> Outbox => Set<OrderPaymentOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SaleDbContext).Assembly);
}
