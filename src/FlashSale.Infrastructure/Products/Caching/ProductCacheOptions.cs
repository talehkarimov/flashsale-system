namespace FlashSale.Infrastructure.Products.Caching;

public sealed class ProductCacheOptions
{
    public const string SectionName = "ProductCache";

    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
}
