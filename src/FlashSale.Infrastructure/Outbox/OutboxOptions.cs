namespace FlashSale.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool WorkerEnabled { get; set; } = true;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);
}
