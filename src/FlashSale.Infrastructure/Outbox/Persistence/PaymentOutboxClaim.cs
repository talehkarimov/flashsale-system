namespace FlashSale.Infrastructure.Outbox.Persistence;

public sealed record PaymentOutboxClaim(Guid MessageId, Guid OrderId, Guid LeaseToken, int Attempts);
