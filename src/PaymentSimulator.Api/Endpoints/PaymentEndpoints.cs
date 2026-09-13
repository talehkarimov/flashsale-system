using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Endpoints;

internal static class PaymentEndpoints
{
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
