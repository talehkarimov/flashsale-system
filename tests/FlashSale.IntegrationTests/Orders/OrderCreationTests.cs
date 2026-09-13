using System.Net;
using System.Net.Http.Json;
using FlashSale.Api.Contracts.Orders;
using FlashSale.Domain.Orders;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Orders;

[Collection("SQL")]
public sealed class OrderCreationTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creation_persists_pending_order_reservation_and_outbox()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client, quantity: 3);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await ReadAsync(response);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(59.97m, order.Total);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location!.OriginalString);
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(order.Id, (await ReadAsync(get)).Id);
        await using var db = sql.SaleDb();
        Assert.Equal(97, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        Assert.Equal(order.Id, (await db.Outbox.SingleAsync()).OrderId);
        await using var payments = sql.PaymentDb();
        Assert.Empty(await payments.Operations.ToListAsync());
    }

    [Fact]
    public async Task Insufficient_inventory_rolls_back_all_items_and_order()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client, items: [new(SqlFixture.ProductId, 2), new(SqlFixture.SecondProductId, 1)]);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertEmptyOrdersAsync();
    }

    [Fact]
    public async Task Outbox_insert_failure_rolls_back_order_and_reservation()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        await using var db = sql.SaleDb();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectOutbox ON OutboxMessages AFTER INSERT AS THROW 51000, 'Injected outbox failure', 1;");
        try
        {
            using var response = await CreateAsync(client);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("Injected outbox failure", await response.Content.ReadAsStringAsync());
            await AssertEmptyOrdersAsync();
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectOutbox"); }
    }

    [Fact]
    public async Task Validation_and_missing_order_return_structured_errors()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var invalid = await CreateAsync(client, quantity: 0);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        using var missingKey = await client.PostAsJsonAsync("/orders", new CreateOrderRequest(UserId, [new(SqlFixture.ProductId, 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        using var missing = await client.GetAsync($"/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertEmptyOrdersAsync();
    }

    private async Task AssertEmptyOrdersAsync()
    {
        await using var db = sql.SaleDb();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Outbox.ToListAsync());
        Assert.Equal(100, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
    }
}
