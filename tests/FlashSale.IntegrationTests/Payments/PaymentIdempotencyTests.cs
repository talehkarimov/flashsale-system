using System.Net;
using System.Net.Http.Json;
using FlashSale.Application.Payments.Contracts;
using FlashSale.Infrastructure.Payments.Http;
using FlashSale.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using static FlashSale.IntegrationTests.Support.OrderTestClient;

namespace FlashSale.IntegrationTests.Payments;

[Collection("SQL")]
public sealed class PaymentIdempotencyTests(SqlFixture sql) : IAsyncLifetime
{
    public Task InitializeAsync() => sql.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Provider_serializes_concurrent_pay_and_close_and_retains_outcome_across_restart()
    {
        var request = new PaymentRequest(Guid.NewGuid(), 19.99m, DateTimeOffset.UtcNow.AddMinutes(2));
        PaymentOutcome outcome;
        await using (var simulator = new SimulatorFactory(sql))
        {
            using var client = simulator.CreateClient();
            var provider = new PaymentGatewayHttpClient(client);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 20).Select(async i =>
            {
                await gate.Task;
                return await (i % 2 == 0 ? provider.ClosePaymentAsync(request, CancellationToken.None) : provider.ExecutePaymentAsync(request, CancellationToken.None));
            }).ToArray();
            gate.SetResult();
            var results = await Task.WhenAll(tasks);
            outcome = results[0].Outcome;
            Assert.All(results, x => Assert.Equal(outcome, x.Outcome));
            using var conflict = await client.PostAsJsonAsync("/payments", request with { Amount = 20m });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        await using var restarted = new SimulatorFactory(sql);
        using var replay = restarted.CreateClient();
        Assert.Equal(outcome, (await new PaymentGatewayHttpClient(replay).ExecutePaymentAsync(request, CancellationToken.None)).Outcome);
        await using var payments = sql.PaymentDb();
        var operation = Assert.Single(await payments.Operations.ToListAsync());
        Assert.Equal(21, operation.Requests);
    }
}
