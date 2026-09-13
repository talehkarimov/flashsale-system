using System.Net.Http.Json;
using FlashSale.Api.Contracts.Orders;
using FlashSale.Domain.Orders;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using PaymentSimulator.Api.Configuration;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Payments;

[Collection("SQL")]
public sealed class OrderPaymentTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(SimulatorScenario.Success, OrderStatus.Paid, 99)]
    [InlineData(SimulatorScenario.Reject, OrderStatus.Failed, 100)]
    [InlineData(SimulatorScenario.ChargeThenDelay, OrderStatus.Paid, 99)]
    public async Task Payment_outcomes_are_durable_and_duplicate_processing_is_safe(SimulatorScenario scenario, OrderStatus status, int available)
    {
        await using var simulator = new SimulatorFactory(sql, scenario);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client);
        var created = await ReadAsync(response);
        Assert.True(await ProcessAsync(factory));
        await using var db = sql.SaleDb();
        var order = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(status, order.Status);
        Assert.Equal(status == OrderStatus.Failed, order.InventoryReleased);
        Assert.Equal(available, (await db.Inventory.AsNoTracking().SingleAsync(x => x.ProductId == SqlFixture.ProductId)).AvailableQuantity);
        Assert.NotNull((await db.Outbox.AsNoTracking().SingleAsync()).ProcessedAt);
        await using var payments = sql.PaymentDb();
        var operation = await payments.Operations.AsNoTracking().SingleAsync();
        Assert.Equal(created.Id, operation.OperationId);
        Assert.Equal(scenario == SimulatorScenario.ChargeThenDelay ? 2 : 1, operation.Requests);
        await db.Outbox.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessedAt, (DateTimeOffset?)null));
        Assert.True(await ProcessAsync(factory));
        Assert.Equal(available, (await db.Inventory.AsNoTracking().SingleAsync(x => x.ProductId == SqlFixture.ProductId)).AvailableQuantity);
        Assert.Equal(operation.Requests, (await payments.Operations.AsNoTracking().SingleAsync()).Requests);
    }

    [Fact]
    public async Task Hosted_worker_processes_created_order()
    {
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator, workerEnabled: true);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client);
        var created = await ReadAsync(response);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        OrderResponse? observed;
        do
        {
            await Task.Delay(50, deadline.Token);
            observed = await client.GetFromJsonAsync<OrderResponse>($"/orders/{created.Id}", Json, deadline.Token);
        } while (observed!.Status == OrderStatus.Pending);
        Assert.Equal(OrderStatus.Paid, observed.Status);
    }
}
