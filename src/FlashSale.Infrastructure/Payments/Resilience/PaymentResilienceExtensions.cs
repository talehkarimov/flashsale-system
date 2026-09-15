using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace FlashSale.Infrastructure.Payments.Resilience;

public static class PaymentResilienceExtensions
{
    public static IHttpClientBuilder AddPaymentResilience(this IHttpClientBuilder client, PaymentResilienceOptions settings)
    {
        client.AddStandardResilienceHandler(policy =>
        {
            policy.TotalRequestTimeout.Timeout = settings.TotalTimeout;
            policy.AttemptTimeout.Timeout = settings.AttemptTimeout;
            policy.Retry.MaxRetryAttempts = settings.RetryAttempts;
            policy.Retry.Delay = settings.RetryDelay;
            policy.Retry.BackoffType = DelayBackoffType.Exponential;
            policy.Retry.UseJitter = true;
            policy.CircuitBreaker.SamplingDuration = settings.SamplingDuration;
            policy.CircuitBreaker.MinimumThroughput = settings.MinimumThroughput;
            policy.CircuitBreaker.FailureRatio = settings.FailureRatio;
            policy.CircuitBreaker.BreakDuration = settings.BreakDuration;
        });
        return client;
    }
}
