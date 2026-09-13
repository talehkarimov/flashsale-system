using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlashSale.Application.Payments.Contracts;

namespace FlashSale.Infrastructure.Payments.Http;

public sealed class PaymentGatewayHttpClient(HttpClient client) : IPaymentGateway
{
    private const string ExecuteRoute = "payments";
    private const string CloseRoute = "payments/close";

    public Task<PaymentResult> ExecutePaymentAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        SendPaymentRequestAsync(ExecuteRoute, request, cancellationToken);

    public Task<PaymentResult> ClosePaymentAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        SendPaymentRequestAsync(CloseRoute, request, cancellationToken);

    private async Task<PaymentResult> SendPaymentRequestAsync(
        string route,
        PaymentRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(route, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            var result = await response.Content.ReadFromJsonAsync<ProviderResponse>(cancellationToken);
            return new PaymentResult(result?.Outcome switch
            {
                nameof(PaymentOutcome.Paid) => PaymentOutcome.Paid,
                nameof(PaymentOutcome.Rejected) => PaymentOutcome.Rejected,
                nameof(PaymentOutcome.Cancelled) => PaymentOutcome.Cancelled,
                _ => throw new HttpRequestException("Payment provider returned an unknown outcome.")
            });
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Payment provider returned an invalid response.", exception);
        }
    }

    private sealed record ProviderResponse([property: JsonRequired] string Outcome);
}
