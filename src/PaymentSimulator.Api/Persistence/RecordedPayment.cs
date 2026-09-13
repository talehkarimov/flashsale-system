using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Persistence;

public sealed record RecordedPayment(PaymentOutcome Outcome, bool Created);
