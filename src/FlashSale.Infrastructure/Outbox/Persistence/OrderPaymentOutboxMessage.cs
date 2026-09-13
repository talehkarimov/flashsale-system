namespace FlashSale.Infrastructure.Outbox.Persistence;

public sealed class OrderPaymentOutboxMessage
{
    private OrderPaymentOutboxMessage() { }

    public OrderPaymentOutboxMessage(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("An order is required.", nameof(orderId));
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
}
