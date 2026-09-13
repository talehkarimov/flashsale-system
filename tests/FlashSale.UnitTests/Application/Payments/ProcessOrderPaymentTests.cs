using FlashSale.Application.Orders.GetOrder;
using FlashSale.Application.Payments.Contracts;
using FlashSale.Application.Payments.ProcessOrderPayment;
using FlashSale.Domain.Orders;

namespace FlashSale.UnitTests.Application.Payments;

public sealed class ProcessOrderPaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PaymentOutcome.Paid, OrderStatus.Paid, false)]
    [InlineData(PaymentOutcome.Rejected, OrderStatus.Failed, true)]
    [InlineData(PaymentOutcome.Cancelled, OrderStatus.Expired, true)]
    public async Task Confirmed_outcomes_determine_transition_and_compensation(
        PaymentOutcome outcome, OrderStatus status, bool release)
    {
        var order = NewOrder();
        var persistence = new Persistence(order);
        var gateway = new Gateway(outcome);
        var handler = new ProcessOrderPaymentHandler(persistence, gateway, persistence, new Clock(order.ExpiresAt));

        await handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.True(gateway.Closed);
        Assert.Equal(new PaymentRequest(order.Id, order.Total, order.ExpiresAt), gateway.Request);
        Assert.Equal(status, order.Status);
        Assert.Equal(release, persistence.ReleaseRequested);
        Assert.Equal(release, order.InventoryReleased);
    }

    [Fact]
    public async Task Before_deadline_uses_pay_with_stable_order_identity()
    {
        var order = NewOrder();
        var persistence = new Persistence(order);
        var gateway = new Gateway(PaymentOutcome.Paid);
        var handler = new ProcessOrderPaymentHandler(persistence, gateway, persistence, new Clock(Now));

        await handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.False(gateway.Closed);
        Assert.Equal(order.Id, gateway.Request!.OperationId);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public async Task Completion_uses_current_locked_state_when_another_worker_finishes_during_http()
    {
        var order = NewOrder();
        var persistence = new Persistence(order) { BeforeCompletion = order.MarkAsPaid };
        var gateway = new Gateway(PaymentOutcome.Cancelled);
        var handler = new ProcessOrderPaymentHandler(persistence, gateway, persistence, new Clock(order.ExpiresAt));

        await handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.False(persistence.ReleaseRequested);
        Assert.False(order.InventoryReleased);
    }

    [Fact]
    public async Task Ambiguous_payment_does_not_complete_or_release_inventory()
    {
        var order = NewOrder();
        var persistence = new Persistence(order);
        var gateway = new Gateway(PaymentOutcome.Paid) { Failure = new HttpRequestException("Lost response") };
        var handler = new ProcessOrderPaymentHandler(persistence, gateway, persistence, new Clock(Now));

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(order.Id, CancellationToken.None));

        Assert.False(persistence.Completed);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.False(order.InventoryReleased);
    }

    [Fact]
    public async Task Terminal_replay_completes_work_without_calling_provider_or_compensating_again()
    {
        var order = NewOrder();
        order.MarkAsFailed();
        order.ReleaseInventory();
        var persistence = new Persistence(order);
        var gateway = new Gateway(PaymentOutcome.Paid);
        var handler = new ProcessOrderPaymentHandler(persistence, gateway, persistence, new Clock(Now));

        await handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.Null(gateway.Request);
        Assert.True(persistence.Completed);
        Assert.False(persistence.ReleaseRequested);
        Assert.Equal(OrderStatus.Failed, order.Status);
    }

    private static Order NewOrder() => new(Guid.NewGuid(), new IdempotencyKey("payment"), "hash",
        [new OrderItem(Guid.NewGuid(), 1, 12m)], Now, TimeSpan.FromMinutes(2));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Persistence(Order order) : IOrderReader, IOrderPaymentCompletion
    {
        public Action? BeforeCompletion { get; init; }
        public bool ReleaseRequested { get; private set; }
        public bool Completed { get; private set; }

        public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Order?>(order);

        public Task CompleteAsync(Guid orderId, Func<Order, bool> transition, CancellationToken cancellationToken)
        {
            BeforeCompletion?.Invoke();
            ReleaseRequested = transition(order);
            Completed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Gateway(PaymentOutcome outcome) : IPaymentGateway
    {
        public PaymentRequest? Request { get; private set; }
        public bool Closed { get; private set; }
        public Exception? Failure { get; init; }

        public Task<PaymentResult> ExecutePaymentAsync(PaymentRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Failure is null ? Task.FromResult(new PaymentResult(outcome)) : Task.FromException<PaymentResult>(Failure);
        }

        public Task<PaymentResult> ClosePaymentAsync(PaymentRequest request, CancellationToken cancellationToken)
        {
            Closed = true;
            return ExecutePaymentAsync(request, cancellationToken);
        }
    }
}
