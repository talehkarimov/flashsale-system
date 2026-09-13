using FlashSale.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Outbox;

public sealed class OutboxOptionsValidator(IOptions<PaymentOptions> payment) : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        var failures = new List<string>();
        if (options.PollInterval <= TimeSpan.Zero || options.PollInterval.TotalMilliseconds > int.MaxValue)
        {
            failures.Add("Outbox:PollInterval must be positive and fit the timer range.");
        }

        if (options.LeaseDuration <= payment.Value.Resilience.TotalTimeout ||
            options.LeaseDuration.TotalSeconds > int.MaxValue)
        {
            failures.Add("Outbox:LeaseDuration must exceed the payment total timeout and fit SQL DATEADD.");
        }

        if (options.RetryDelay <= TimeSpan.Zero || options.MaxRetryDelay < options.RetryDelay ||
            options.MaxRetryDelay.TotalSeconds > int.MaxValue)
        {
            failures.Add("Outbox retry delays must be positive, bounded by MaxRetryDelay, and fit SQL DATEADD.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
