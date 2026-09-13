using FlashSale.Infrastructure.Outbox.Processing;
using Microsoft.Extensions.Logging;

namespace FlashSale.Performance;

internal sealed class ProcessorErrorLogger : ILogger<PaymentOutboxProcessor>
{
    internal sealed class Attempt { public bool Failed { get; set; } }
    private readonly AsyncLocal<Attempt?> current = new();

    public Attempt StartAttempt() => current.Value = new Attempt();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning && current.Value is { } attempt) attempt.Failed = true;
    }
}
