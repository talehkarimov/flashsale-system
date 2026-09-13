using System.Net;
using FlashSale.Domain.Orders;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using PaymentSimulator.Api.Configuration;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Payments;

[Collection("SQL")]
public sealed class CompensationTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Compensation_failure_rolls_back_release_and_retries_safely()
    {
        await using var simulator = new SimulatorFactory(sql, SimulatorScenario.Reject);
        await using var factory = new SaleFactory(sql, simulator);
        using var client = factory.CreateClient();
        using var response = await CreateAsync(client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = sql.SaleDb();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectCompletion ON Orders AFTER UPDATE AS THROW 51000, 'Injected completion failure', 1;");
        try
        {
            Assert.True(await ProcessAsync(factory));
            Assert.Equal(OrderStatus.Pending, (await db.Orders.AsNoTracking().SingleAsync()).Status);
            Assert.Equal(99, (await db.Inventory.AsNoTracking().SingleAsync(x => x.ProductId == SqlFixture.ProductId)).AvailableQuantity);
            Assert.Null((await db.Outbox.AsNoTracking().SingleAsync()).ProcessedAt);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectCompletion"); }
        await db.Outbox.ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.True(await ProcessAsync(factory));
        Assert.Equal(OrderStatus.Failed, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(100, (await db.Inventory.AsNoTracking().SingleAsync(x => x.ProductId == SqlFixture.ProductId)).AvailableQuantity);
        Assert.NotNull((await db.Outbox.AsNoTracking().SingleAsync()).ProcessedAt);
    }
}
