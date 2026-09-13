using FlashSale.Infrastructure.Payments.Resilience;

namespace FlashSale.Infrastructure.Payments;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";
    public Uri BaseAddress { get; set; } = null!;
    public PaymentResilienceOptions Resilience { get; set; } = new();
}
