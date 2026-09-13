using FlashSale.Application.Payments.Contracts;
using FlashSale.Domain.Orders;
using FlashSale.Infrastructure.Payments.Http;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Payments;

[Collection("SQL")]
public sealed class OrderExpirationTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Expiration_closes_payment_before_releasing_inventory_and_late_payment_is_cancelled()
    {
        var clock = new ManualClock();
        await using var simulator = new SimulatorFactory(sql);
        await using var factory = new SaleFactory(sql, simulator, clock);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client);
        var created = await ReadAsync(response);
        clock.Now = created.ExpiresAt;
        Assert.True(await ProcessAsync(factory));
        await using var db = sql.SaleDb();
        var order = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.True(order.InventoryReleased);
        Assert.Equal(100, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        using var providerClient = simulator.CreateClient();
        var late = await new PaymentGatewayHttpClient(providerClient).ExecutePaymentAsync(new(created.Id, created.Total, created.ExpiresAt), CancellationToken.None);
        Assert.Equal(PaymentOutcome.Cancelled, late.Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Payment_and_expiration_race_preserves_provider_outcome_and_inventory(bool chargeBeforeExpiration)
    {
        var clock = new ManualClock();
        await using var simulator = new SimulatorFactory(sql);
        using var providerClient = simulator.CreateClient();
        var gated = new GatedPaymentProvider(new PaymentGatewayHttpClient(providerClient), chargeBeforeExpiration);
        await using var first = new SaleFactory(sql, simulator, clock, s => s.AddScoped<IPaymentGateway>(_ => gated));
        await using var second = new SaleFactory(sql, simulator, clock);
        using var client = first.CreateClient();
        using var response = await CreateAsync(client);
        var created = await ReadAsync(response);
        var processing = ProcessAsync(first);
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            clock.Now = created.ExpiresAt;
            await using var db = sql.SaleDb();
            await db.Outbox.ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.True(await ProcessAsync(second));
        }
        finally { gated.Continue.TrySetResult(); }
        await processing;
        await using var verify = sql.SaleDb();
        var order = await verify.Orders.SingleAsync();
        Assert.Equal(chargeBeforeExpiration ? OrderStatus.Paid : OrderStatus.Expired, order.Status);
        Assert.Equal(!chargeBeforeExpiration, order.InventoryReleased);
        Assert.Equal(chargeBeforeExpiration ? 99 : 100, (await verify.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        await using var payments = sql.PaymentDb();
        Assert.Equal(chargeBeforeExpiration ? PaymentSimulator.Api.Payments.PaymentOutcome.Paid : PaymentSimulator.Api.Payments.PaymentOutcome.Cancelled, (await payments.Operations.SingleAsync()).Outcome);
        Assert.NotNull((await verify.Outbox.SingleAsync()).ProcessedAt);
    }
}
