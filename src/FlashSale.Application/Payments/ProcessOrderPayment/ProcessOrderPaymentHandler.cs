using FlashSale.Application.Orders.GetOrder;
using FlashSale.Application.Payments.Contracts;
using FlashSale.Domain.Orders;

namespace FlashSale.Application.Payments.ProcessOrderPayment;

public sealed class ProcessOrderPaymentHandler(
    IOrderReader orders, IPaymentGateway payments, IOrderPaymentCompletion completion, TimeProvider clock)
{
    public async Task HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException("The payment order is missing.");
        PaymentResult? result = null;
        if (order.Status == OrderStatus.Pending)
        {
            var request = new PaymentRequest(order.Id, order.Total, order.ExpiresAt);
            result = clock.GetUtcNow() >= order.ExpiresAt
                ? await payments.ClosePaymentAsync(request, cancellationToken)
                : await payments.ExecutePaymentAsync(request, cancellationToken);
        }

        await completion.CompleteAsync(orderId, lockedOrder => ApplyOutcome(lockedOrder, result), cancellationToken);
    }

    private bool ApplyOutcome(Order order, PaymentResult? payment)
    {
        if (order.Status != OrderStatus.Pending)
        {
            return false;
        }

        switch (payment?.Outcome ?? throw new InvalidOperationException("A pending order requires a payment outcome."))
        {
            case PaymentOutcome.Paid:
                order.MarkAsPaid();
                break;
            case PaymentOutcome.Rejected:
                order.MarkAsFailed();
                break;
            case PaymentOutcome.Cancelled:
                order.Expire(clock.GetUtcNow());
                break;
            default:
                throw new InvalidOperationException("Unknown payment outcome.");
        }

        return order.Status is OrderStatus.Failed or OrderStatus.Expired && order.ReleaseInventory();
    }
}
