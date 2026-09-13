namespace PaymentSimulator.Api.Payments;

public sealed class PaymentOperation
{
    public const int AmountPrecision = 18;
    public const int AmountScale = 2;
    public const decimal MaximumAmount = 9999999999999999.99m;

    private PaymentOperation() { }

    public PaymentOperation(PaymentRequest request, PaymentOutcome outcome)
    {
        PaymentRequestValidator.Validate(request);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        OperationId = request.OperationId;
        Amount = request.Amount;
        ExpiresAt = request.ExpiresAt;
        Outcome = outcome;
    }

    public Guid OperationId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public PaymentOutcome Outcome { get; private set; }
    public int Requests { get; private set; }

    public void RecordRequest(PaymentRequest request)
    {
        if (OperationId != request.OperationId || Amount != request.Amount || ExpiresAt != request.ExpiresAt)
        {
            throw new PaymentException(PaymentError.OperationConflict, "Payment operation parameters conflict.");
        }

        Requests = checked(Requests + 1);
    }

    public static bool IsValidAmount(decimal amount) =>
        amount > 0 && amount <= MaximumAmount && decimal.Round(amount, AmountScale) == amount;
}
