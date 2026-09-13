namespace PaymentSimulator.Api.Payments;

public sealed record PaymentRequest(Guid OperationId, decimal Amount, DateTimeOffset ExpiresAt);
