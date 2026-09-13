using System.Net;
using FlashSale.Domain.Orders;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Concurrency;

[Collection("SQL")]
public sealed class InventoryConcurrencyTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Stress")]
    public async Task One_thousand_attempts_across_instances_reserve_exactly_one_hundred_units()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var first = new SaleFactory(sql, simulator);
        await using var second = new SaleFactory(sql, simulator);
        using var a = first.CreateClient();
        using var b = second.CreateClient();
        a.Timeout = b.Timeout = TimeSpan.FromMinutes(3);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 1000).Select(async i =>
        {
            await gate.Task;
            using var response = await CreateAsync(i % 2 == 0 ? a : b, $"stress-{i}");
            return response.StatusCode;
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(100, results.Count(x => x == HttpStatusCode.Created));
        Assert.Equal(900, results.Count(x => x == HttpStatusCode.Conflict));
        await using var db = sql.SaleDb();
        Assert.Equal(0, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        Assert.All(await db.Inventory.ToListAsync(), x => Assert.True(x.AvailableQuantity >= 0));
        var orders = await db.Orders.ToListAsync();
        Assert.Equal(100, orders.Count);
        Assert.Equal(100, orders.Sum(x => x.Items.Sum(i => i.Quantity)));
        Assert.All(orders, x => Assert.Equal(OrderStatus.Pending, x.Status));
        Assert.Equal(100, await db.Outbox.CountAsync());
        Assert.Equal(100, orders.Select(x => x.IdempotencyKey).Distinct().Count());
        Assert.Equal(100, await db.Outbox.Select(x => x.OrderId).Distinct().CountAsync());
    }
}
