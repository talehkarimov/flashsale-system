using System.Text.Json;
using System.IO;
using FlashSale.Application.Products.GetProduct;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Products.Caching;

public sealed class CachedProductReader(
    IProductReader database,
    IDistributedCache cache,
    IOptions<ProductCacheOptions> options,
    ProductCacheMetrics metrics) : IProductReader
{
    private const string KeyPrefix = "flashsale:product:v1:";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductReadModel?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var key = KeyPrefix + id.ToString("N");
        string? cached = null;
        var cacheReadFailed = false;
        try
        {
            cached = await WithTimeout(token => cache.GetStringAsync(key, token), cancellationToken);
        }
        catch (Exception exception) when (IsCacheFailure(exception, cancellationToken))
        {
            metrics.ReadFailure();
            cacheReadFailed = true;
        }

        if (cached is not null)
        {
            try
            {
                var product = JsonSerializer.Deserialize<ProductReadModel>(cached, SerializerOptions);
                if (product is not null)
                {
                    metrics.Hit();
                    return product;
                }
            }
            catch (JsonException)
            {
                metrics.ReadFailure();
            }
        }
        else
        {
            metrics.Miss();
        }

        metrics.DatabaseFallback();
        var result = await database.FindAsync(id, cancellationToken);
        if (result is null)
        {
            return null;
        }

        if (!cacheReadFailed)
        {
            try
            {
                var value = JsonSerializer.Serialize(result, SerializerOptions);
                await WithTimeout(token => cache.SetStringAsync(key, value,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.Ttl }, token), cancellationToken);
            }
            catch (Exception exception) when (IsCacheFailure(exception, cancellationToken))
            {
                metrics.WriteFailure();
            }
        }

        return result;
    }

    private async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.OperationTimeout);
        var task = operation(timeout.Token);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token));
        if (completed == task)
        {
            return await task;
        }

        _ = task.ContinueWith(finished => _ = finished.Exception, TaskContinuationOptions.OnlyOnFaulted);
        throw new TimeoutException("The product cache operation exceeded its timeout.");
    }

    private async Task WithTimeout(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        await WithTimeout(async token => { await operation(token); return true; }, cancellationToken);

    private static bool IsCacheFailure(Exception exception, CancellationToken callerToken) =>
        !callerToken.IsCancellationRequested && exception is TimeoutException or RedisException or IOException;
}
