using FlashSale.Domain.Products;
using FlashSale.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentSimulator.Api.Persistence;

namespace FlashSale.IntegrationTests.Support;

public sealed class SqlFixture : IAsyncLifetime
{
    public string SaleConnection { get; } = Connection("FlashSaleTests_");
    public string PaymentConnection { get; } = Connection("FlashSalePaymentTests_");
    public static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid SecondProductId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static string Connection(string prefix)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FLASHSALE_TEST_SQL") ??
            "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
        {
            InitialCatalog = prefix + Guid.NewGuid().ToString("N"),
            MaxPoolSize = 150
        };
        return connection.ConnectionString;
    }

    public SaleDbContext SaleDb() => new(new DbContextOptionsBuilder<SaleDbContext>().UseSqlServer(SaleConnection).Options);
    public PaymentDbContext PaymentDb() => new(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlServer(PaymentConnection).Options);

    public async Task InitializeAsync()
    {
        await using var sale = SaleDb();
        await sale.Database.MigrateAsync();
        await using var payment = PaymentDb();
        await payment.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = SaleDb();
        await db.Outbox.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.Inventory.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        db.Products.AddRange(new Product(ProductId, "Flash product", 19.99m), new Product(SecondProductId, "Sold out product", 5m));
        db.Inventory.AddRange(new FlashSale.Domain.Inventory.Inventory(ProductId, 100), new FlashSale.Domain.Inventory.Inventory(SecondProductId, 0));
        await db.SaveChangesAsync();
        await using var payments = PaymentDb();
        await payments.Operations.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await using var sale = SaleDb();
        await sale.Database.EnsureDeletedAsync();
        await using var payment = PaymentDb();
        await payment.Database.EnsureDeletedAsync();
    }
}
