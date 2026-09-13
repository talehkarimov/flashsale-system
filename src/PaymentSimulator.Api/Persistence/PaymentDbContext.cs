using Microsoft.EntityFrameworkCore;
using PaymentSimulator.Api.Payments;
using PaymentSimulator.Api.Persistence.Configurations;

namespace PaymentSimulator.Api.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentOperation> Operations => Set<PaymentOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new PaymentOperationConfiguration());
}
