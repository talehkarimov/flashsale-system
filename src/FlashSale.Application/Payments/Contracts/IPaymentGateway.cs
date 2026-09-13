namespace FlashSale.Application.Payments.Contracts;

public interface IPaymentGateway
{
    Task<PaymentResult> ExecutePaymentAsync(PaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentResult> ClosePaymentAsync(PaymentRequest request, CancellationToken cancellationToken);
}
