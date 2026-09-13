using FlashSale.Application.Payments.Contracts;

namespace FlashSale.IntegrationTests.Support;

internal sealed class GatedPaymentProvider(IPaymentGateway inner, bool chargeFirst) : IPaymentGateway
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<PaymentResult> ExecutePaymentAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var result = chargeFirst ? await inner.ExecutePaymentAsync(request, cancellationToken) : null;
        Entered.TrySetResult();
        await Continue.Task.WaitAsync(cancellationToken);
        return result ?? await inner.ExecutePaymentAsync(request, cancellationToken);
    }

    public Task<PaymentResult> ClosePaymentAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        inner.ClosePaymentAsync(request, cancellationToken);
}
