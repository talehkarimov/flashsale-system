using FlashSale.Domain.Orders;

namespace FlashSale.Application.Payments.ProcessOrderPayment;

public interface IOrderPaymentCompletion
{
    // Invokes the transition on the current, exclusively locked order. A true return requests inventory release.
    // Persists the transition, compensation, and payment Outbox completion in one transaction.
    Task CompleteAsync(Guid orderId, Func<Order, bool> transition, CancellationToken cancellationToken);
}
