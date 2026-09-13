using System.Net;
using FlashSale.Domain.Orders;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Orders;

[Collection("SQL")]
public sealed class OrderIdempotencyTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Replay_returns_original_order_and_conflicting_payload_is_rejected()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var first = await CreateAsync(client, "same");
        using var retry = await CreateAsync(client, "same");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await ReadAsync(first)).Id, (await ReadAsync(retry)).Id);
        using var conflict = await CreateAsync(client, "same", 2);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var db = sql.SaleDb();
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Equal(99, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_across_instances_reserve_once_even_when_stock_is_exhausted()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var first = new SaleFactory(sql, simulator);
        await using var second = new SaleFactory(sql, simulator);
        using var a = first.CreateClient();
        using var b = second.CreateClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 40).Select(async i =>
        {
            await gate.Task;
            using var response = await CreateAsync(i % 2 == 0 ? a : b, "concurrent", 100);
            return (response.StatusCode, Order: await ReadAsync(response));
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(39, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(results.Select(x => x.Order.Id).Distinct());
        await using var db = sql.SaleDb();
        Assert.Equal(0, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Single(await db.Outbox.ToListAsync());
    }
}
