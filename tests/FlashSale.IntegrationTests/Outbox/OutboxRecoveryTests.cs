using System.Net;
using FlashSale.Application.Payments.Contracts;
using FlashSale.Domain.Orders;
using FlashSale.Infrastructure.Payments.Http;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSimulator.Api.Configuration;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Outbox;

[Collection("SQL")]
public sealed class OutboxRecoveryTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unavailable_provider_keeps_reservation_and_outbox_until_restart_recovery()
    {
        await using var unavailable = new SimulatorFactory(sql, SimulatorScenario.Unavailable);
        await using (var factory = new SaleFactory(sql, unavailable))
        {
            using var client = factory.CreateClient();
            using var response = await CreateAsync(client);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True(await ProcessAsync(factory));
        }
        await using var db = sql.SaleDb();
        Assert.Equal(OrderStatus.Pending, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(99, (await db.Inventory.FindAsync(SqlFixture.ProductId))!.AvailableQuantity);
        var message = await db.Outbox.AsNoTracking().SingleAsync();
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.LeaseToken);
        Assert.True(message.NextAttemptAt > DateTimeOffset.UtcNow);
        await db.Outbox.ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await using var recovered = new SimulatorFactory(sql);
        await using var restarted = new SaleFactory(sql, recovered);
        Assert.True(await ProcessAsync(restarted));
        Assert.Equal(OrderStatus.Paid, (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancelled_worker_leaves_claim_for_lease_recovery()
    {
        await using var simulator = new SimulatorFactory(sql);
        using var providerClient = simulator.CreateClient();
        var gated = new GatedPaymentProvider(new PaymentGatewayHttpClient(providerClient), false);
        await using var first = new SaleFactory(sql, simulator, configure: s => s.AddScoped<IPaymentGateway>(_ => gated));
        using var client = first.CreateClient();
        using var response = await CreateAsync(client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var cancellation = new CancellationTokenSource();
        var processing = ProcessAsync(first, cancellation.Token);
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await using var db = sql.SaleDb();
        var message = await db.Outbox.AsNoTracking().SingleAsync();
        Assert.NotNull(message.LeaseToken);
        Assert.Null(message.ProcessedAt);
        await using var restarted = new SaleFactory(sql, simulator);
        Assert.False(await ProcessAsync(restarted));
        await db.Outbox.ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.True(await ProcessAsync(restarted));
        Assert.Equal(OrderStatus.Paid, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2, (await db.Outbox.AsNoTracking().SingleAsync()).Attempts);
    }
}
