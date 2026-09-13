using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlashSale.Application.Payments.Contracts;
using FlashSale.Infrastructure.Payments;
using FlashSale.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FlashSale.IntegrationTests.Payments;

public sealed class PaymentResilienceTests
{
    private static ServiceProvider Services(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Payment:BaseAddress"] = "http://localhost/" })
            .Build();
        services.AddPayments(configuration).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static PaymentRequest Request() => new(Guid.NewGuid(), 19.99m, DateTimeOffset.UtcNow.AddMinutes(2));

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transient_response_is_retried_with_same_operation(int status)
    {
        var operations = new List<Guid>();
        var handler = new ScriptedHandler(async (request, _, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            operations.Add(body.RootElement.GetProperty("operationId").GetGuid());
            return operations.Count < 3 ? new HttpResponseMessage((HttpStatusCode)status) : Paid();
        });
        await using var services = Services(handler);
        var request = Request();
        var result = await services.GetRequiredService<IPaymentGateway>().ExecutePaymentAsync(request, CancellationToken.None);
        Assert.Equal(PaymentOutcome.Paid, result.Outcome);
        Assert.Equal(3, handler.Attempts);
        Assert.All(operations, id => Assert.Equal(request.OperationId, id));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(409)]
    public async Task Nontransient_response_is_not_retried(int status)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        await using var services = Services(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => services.GetRequiredService<IPaymentGateway>()
            .ExecutePaymentAsync(Request(), CancellationToken.None));
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Rejection_is_a_terminal_business_outcome()
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { outcome = "Rejected" })
        }));
        await using var services = Services(handler);
        Assert.Equal(PaymentOutcome.Rejected, (await services.GetRequiredService<IPaymentGateway>()
            .ExecutePaymentAsync(Request(), CancellationToken.None)).Outcome);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Timeout_has_bounded_attempts()
    {
        var handler = new ScriptedHandler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Paid();
        });
        await using var services = Services(handler);
        var call = services.GetRequiredService<IPaymentGateway>().ExecutePaymentAsync(Request(), CancellationToken.None);
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => call.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(3, handler.Attempts);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"outcome\":null}")]
    [InlineData("{\"outcome\":\"Unknown\"}")]
    [InlineData("{\"outcome\":0}")]
    public async Task Missing_or_invalid_outcome_cannot_be_interpreted_as_paid(string body)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        }));
        await using var services = Services(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => services.GetRequiredService<IPaymentGateway>()
            .ExecutePaymentAsync(Request(), CancellationToken.None));
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Caller_cancellation_stops_work_without_retry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Paid();
        });
        await using var services = Services(handler);
        using var cancellation = new CancellationTokenSource();
        var call = services.GetRequiredService<IPaymentGateway>().ExecutePaymentAsync(Request(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Circuit_opens_and_stops_outbound_attempts()
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var services = Services(handler);
        var provider = services.GetRequiredService<IPaymentGateway>();
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.ExecutePaymentAsync(Request(), CancellationToken.None));
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() => provider.ExecutePaymentAsync(Request(), CancellationToken.None));
        var attempts = handler.Attempts;
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(() => provider.ExecutePaymentAsync(Request(), CancellationToken.None));
        Assert.Equal(10, attempts);
        Assert.Equal(attempts, handler.Attempts);
    }

    private static HttpResponseMessage Paid() => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { outcome = "Paid" }) };
}
