using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlashSale.Performance;

internal sealed class TransactionTimings : DbTransactionInterceptor
{
    private long started;
    public string Label { get; set; } = "other";
    public List<object> Samples { get; } = [];

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        started = Stopwatch.GetTimestamp();
        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Samples.Add(new { label = Label, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
        return Task.CompletedTask;
    }
}
