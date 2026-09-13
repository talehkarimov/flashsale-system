using FlashSale.Application.Payments.ProcessOrderPayment;
using FlashSale.Infrastructure.Outbox.Persistence;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Outbox.Processing;

public sealed class PaymentOutboxProcessor(SqlPaymentOutboxStore store, ProcessOrderPaymentHandler payments, ILogger<PaymentOutboxProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claim = await store.ClaimNextAsync(cancellationToken);
        if (claim is null)
        {
            return false;
        }

        try
        {
            await payments.HandleAsync(claim.OrderId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Payment processing failed for order {OrderId}, Outbox message {MessageId}, attempt {Attempt}",
                claim.OrderId, claim.MessageId, claim.Attempts);
            if (!await store.ScheduleRetryAsync(claim, cancellationToken))
            {
                logger.LogDebug("Outbox claim {MessageId} is no longer owned by this worker", claim.MessageId);
            }
        }

        return true;
    }
}
