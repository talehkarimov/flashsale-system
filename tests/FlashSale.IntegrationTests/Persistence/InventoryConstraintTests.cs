using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Persistence;

[Collection("SQL")]
public sealed class InventoryConstraintTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Database_constraint_rejects_negative_inventory()
    {
        await using var db = sql.SaleDb();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Inventory SET AvailableQuantity = -1 WHERE ProductId = {SqlFixture.ProductId}"));
        Assert.Equal(100, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
    }
}
