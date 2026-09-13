namespace PaymentSimulator.Api.Payments;

public sealed class PaymentException(PaymentError error, string message) : Exception(message)
{
    public PaymentError Error { get; } = error;
}
