namespace FlashSale.Application.Payments.Contracts;

public sealed record PaymentRequest(Guid OperationId, decimal Amount, DateTimeOffset ExpiresAt);
