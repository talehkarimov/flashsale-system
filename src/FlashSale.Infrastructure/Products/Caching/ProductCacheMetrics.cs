namespace FlashSale.Infrastructure.Products.Caching;

public sealed class ProductCacheMetrics
{
    private long hits;
    private long misses;
    private long readFailures;
    private long writeFailures;
    private long databaseFallbacks;

    public void Hit() => Interlocked.Increment(ref hits);
    public void Miss() => Interlocked.Increment(ref misses);
    public void ReadFailure() => Interlocked.Increment(ref readFailures);
    public void WriteFailure() => Interlocked.Increment(ref writeFailures);
    public void DatabaseFallback() => Interlocked.Increment(ref databaseFallbacks);

    public ProductCacheMetricSnapshot Snapshot() => new(
        Interlocked.Read(ref hits), Interlocked.Read(ref misses), Interlocked.Read(ref readFailures),
        Interlocked.Read(ref writeFailures), Interlocked.Read(ref databaseFallbacks));
}

public sealed record ProductCacheMetricSnapshot(long Hits, long Misses, long ReadFailures, long WriteFailures, long DatabaseFallbacks);
