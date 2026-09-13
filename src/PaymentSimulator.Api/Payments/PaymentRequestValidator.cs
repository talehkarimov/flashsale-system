namespace PaymentSimulator.Api.Payments;

public static class PaymentRequestValidator
{
    public static void Validate(PaymentRequest request)
    {
        if (request.OperationId == Guid.Empty)
        {
            throw new PaymentException(PaymentError.InvalidRequest, "OperationId is required.");
        }

        if (!PaymentOperation.IsValidAmount(request.Amount))
        {
            throw new PaymentException(PaymentError.InvalidRequest, "Amount must be positive with at most two decimal places.");
        }

        if (request.ExpiresAt == default)
        {
            throw new PaymentException(PaymentError.InvalidRequest, "ExpiresAt is required.");
        }
    }
}
