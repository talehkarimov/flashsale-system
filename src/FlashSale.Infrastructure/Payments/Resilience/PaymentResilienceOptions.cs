namespace FlashSale.Infrastructure.Payments.Resilience;

public sealed class PaymentResilienceOptions
{
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int RetryAttempts { get; set; } = 2;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(20);
    public int MinimumThroughput { get; set; } = 10;
    public double FailureRatio { get; set; } = 0.5;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(10);
}
