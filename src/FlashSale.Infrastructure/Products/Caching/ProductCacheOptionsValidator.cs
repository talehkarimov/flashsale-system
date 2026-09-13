using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Products.Caching;

public sealed class ProductCacheOptionsValidator : IValidateOptions<ProductCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductCacheOptions options)
    {
        var failures = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("ProductCache:ConnectionString is required when ProductCache:Enabled is true.");
        }
        if (options.Ttl <= TimeSpan.Zero || options.Ttl > TimeSpan.FromHours(24))
        {
            failures.Add("ProductCache:Ttl must be greater than zero and no more than 24 hours.");
        }
        if (options.OperationTimeout <= TimeSpan.Zero || options.OperationTimeout > TimeSpan.FromSeconds(5))
        {
            failures.Add("ProductCache:OperationTimeout must be greater than zero and no more than five seconds.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
