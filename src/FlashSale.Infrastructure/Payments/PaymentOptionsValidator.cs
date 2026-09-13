using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Payments;

public sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromSeconds(1);

    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        var failures = new List<string>();
        var address = options.BaseAddress;
        if (address is null || !address.IsAbsoluteUri)
        {
            failures.Add("Payment:BaseAddress must be an absolute URI.");
        }
        else
        {
            if (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
            {
                failures.Add("Payment:BaseAddress requires HTTPS except for loopback development endpoints.");
            }

            if (address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
            {
                failures.Add("Payment:BaseAddress cannot contain credentials, a query, or a fragment.");
            }

            if (!address.AbsolutePath.EndsWith('/'))
            {
                failures.Add("Payment:BaseAddress must end with a slash.");
            }
        }

        var resilience = options.Resilience;
        if (resilience.AttemptTimeout < MinimumTimeout || resilience.TotalTimeout < resilience.AttemptTimeout)
        {
            failures.Add("Payment timeouts must be at least one second and TotalTimeout must cover AttemptTimeout.");
        }

        if (resilience.RetryAttempts < 0 || resilience.RetryDelay < TimeSpan.Zero)
        {
            failures.Add("Payment retries require a nonnegative attempt count and delay.");
        }

        if (resilience.SamplingDuration < resilience.AttemptTimeout * 2)
        {
            failures.Add("Payment:Resilience:SamplingDuration must be at least twice AttemptTimeout.");
        }

        if (resilience.MinimumThroughput < 2 || !double.IsFinite(resilience.FailureRatio) ||
            resilience.FailureRatio is <= 0 or > 1 || resilience.BreakDuration < MinimumTimeout)
        {
            failures.Add("Payment circuit breaker settings are invalid.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
