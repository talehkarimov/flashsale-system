using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Endpoints;

internal static class PaymentEndpoints
{
    /// <summary>Exposes idempotent payment execution and closure by operationId.</summary>
    /// <remarks>Retries must preserve amount and expiresAt; operationId is the idempotency key.
    /// POST /payments records Paid, Rejected, or Cancelled; new expired operations are Cancelled.
    /// POST /payments/close preserves an existing outcome or records Cancelled; it never refunds.
    /// The first recorded outcome wins, even if its response is delayed or lost.</remarks>
    /// <response code="200">The recorded outcome, including on replay.</response>
    /// <response code="400">Invalid payment request.</response>
    /// <response code="409">The operationId was reused with a different amount or expiry.</response>
    /// <response code="503">POST /payments is configured as unavailable, including for replays.</response>
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var payments = endpoints.MapGroup("/payments");
        payments.MapPost(string.Empty, async (PaymentRequest request, PaymentProcessor service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ExecutePaymentAsync(request, cancellationToken)));
        payments.MapPost("/close", async (PaymentRequest request, PaymentProcessor service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ClosePaymentAsync(request, cancellationToken)));
        return endpoints;
    }
}
