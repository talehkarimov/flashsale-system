using FlashSale.Application.Products.GetProduct;
using FlashSale.Infrastructure.Products.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace FlashSale.UnitTests.Products;

public sealed class CachedProductReaderTests
{
    private static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly ProductReadModel Product = new(ProductId, "Flash product", 19.99m);

    [Fact]
    public async Task Miss_reads_sql_and_populates_cache()
    {
        var cache = new TestCache();
        var database = new TestReader(Product);
        var metrics = new ProductCacheMetrics();
        var reader = Reader(database, cache, metrics);

        var result = await reader.FindAsync(ProductId, default);

        Assert.Equal(Product, result);
        Assert.Equal(1, database.Reads);
        Assert.Single(cache.Values);
        Assert.Equal(1, metrics.Snapshot().Misses);
        Assert.Equal(1, metrics.Snapshot().DatabaseFallbacks);
    }

    [Fact]
    public async Task Hit_does_not_read_sql()
    {
        var cache = new TestCache();
        var database = new TestReader(Product);
        var metrics = new ProductCacheMetrics();
        var reader = Reader(database, cache, metrics);
        await reader.FindAsync(ProductId, default);

        var result = await reader.FindAsync(ProductId, default);

        Assert.Equal(Product, result);
        Assert.Equal(1, database.Reads);
        Assert.Equal(1, metrics.Snapshot().Hits);
    }

    [Fact]
    public async Task Read_failure_falls_back_to_sql()
    {
        var cache = new TestCache { FailReads = true };
        var metrics = new ProductCacheMetrics();
        var reader = Reader(new TestReader(Product), cache, metrics);

        Assert.Equal(Product, await reader.FindAsync(ProductId, default));
        Assert.Equal(1, metrics.Snapshot().ReadFailures);
    }

    [Fact]
    public async Task Write_failure_does_not_fail_sql_result()
    {
        var cache = new TestCache { FailWrites = true };
        var metrics = new ProductCacheMetrics();
        var reader = Reader(new TestReader(Product), cache, metrics);

        Assert.Equal(Product, await reader.FindAsync(ProductId, default));
        Assert.Equal(1, metrics.Snapshot().WriteFailures);
    }

    [Fact]
    public async Task Unexpected_cache_exception_is_not_hidden()
    {
        var cache = new TestCache { ReadException = new InvalidOperationException("cache bug") };
        var reader = Reader(new TestReader(Product), cache, new ProductCacheMetrics());

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.FindAsync(ProductId, default));
    }

    [Fact]
    public async Task Missing_product_is_not_cached_as_a_product()
    {
        var cache = new TestCache();
        var reader = Reader(new TestReader(null), cache, new ProductCacheMetrics());

        Assert.Null(await reader.FindAsync(ProductId, default));
        Assert.Empty(cache.Values);
    }

    [Fact]
    public async Task Expired_entry_causes_sql_reload()
    {
        var cache = new TestCache();
        var database = new TestReader(Product);
        var reader = Reader(database, cache, new ProductCacheMetrics(), TimeSpan.FromMilliseconds(10));
        await reader.FindAsync(ProductId, default);
        await Task.Delay(30);

        await reader.FindAsync(ProductId, default);

        Assert.Equal(2, database.Reads);
    }

    [Fact]
    public async Task Concurrent_cold_misses_are_safe_without_single_flight()
    {
        var cache = new TestCache();
        var database = new TestReader(Product, TimeSpan.FromMilliseconds(10));
        var reader = Reader(database, cache, new ProductCacheMetrics());

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => reader.FindAsync(ProductId, default)));

        Assert.All(results, result => Assert.Equal(Product, result));
        Assert.True(database.Reads > 1);
    }

    private static CachedProductReader Reader(TestReader database, TestCache cache, ProductCacheMetrics metrics, TimeSpan? ttl = null) =>
        new(database, cache, Options.Create(new ProductCacheOptions
        {
            Enabled = true, Ttl = ttl ?? TimeSpan.FromMinutes(5), OperationTimeout = TimeSpan.FromSeconds(1)
        }), metrics);

    private sealed class TestReader(ProductReadModel? value, TimeSpan? delay = null) : IProductReader
    {
        public int Reads { get; private set; }
        public Task<ProductReadModel?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            Reads++;
            if (delay is { } wait) return Delayed(wait);
            return Task.FromResult(value);
        }
        private async Task<ProductReadModel?> Delayed(TimeSpan wait) { await Task.Delay(wait); return value; }
    }

    private sealed class TestCache : IDistributedCache
    {
        public Dictionary<string, (byte[] Value, DateTimeOffset? Expires)> Entries { get; } = [];
        public IEnumerable<byte[]> Values => Entries.Values.Select(entry => entry.Value);
        public bool FailReads { get; init; }
        public bool FailWrites { get; init; }
        public Exception? ReadException { get; init; }
        public Exception? WriteException { get; init; }
        public byte[]? Get(string key)
        {
            if (ReadException is not null) throw ReadException;
            if (FailReads) throw new TimeoutException();
            if (!Entries.TryGetValue(key, out var entry)) return null;
            if (entry.Expires <= DateTimeOffset.UtcNow) { Entries.Remove(key); return null; }
            return entry.Value;
        }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (WriteException is not null) throw WriteException;
            if (FailWrites) throw new TimeoutException();
            Entries[key] = (value, options.AbsoluteExpirationRelativeToNow is { } ttl ? DateTimeOffset.UtcNow + ttl : null);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Entries.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    }
}
