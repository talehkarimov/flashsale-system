using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Application.Orders.GetOrder;
using FlashSale.Application.Payments.ProcessOrderPayment;
using FlashSale.Application.Products.GetProduct;
using FlashSale.Infrastructure.Inventory;
using FlashSale.Infrastructure.Outbox;
using FlashSale.Infrastructure.Payments;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Products.Caching;
using FlashSale.Infrastructure.Products.Persistence;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlashSale(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence(configuration);
        services.AddInventory(configuration);
        services.AddOutbox(configuration);
        services.AddPayments(configuration);
        services.AddOptions<ProductCacheOptions>()
            .Bind(configuration.GetSection(ProductCacheOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ProductCacheOptions>, ProductCacheOptionsValidator>();
        services.AddSingleton<ProductCacheMetrics>();
        var cacheOptions = configuration.GetSection(ProductCacheOptions.SectionName).Get<ProductCacheOptions>() ?? new ProductCacheOptions();
        if (cacheOptions.Enabled)
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = cacheOptions.ConnectionString);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        services.AddScoped<SqlProductReader>();
        services.AddScoped<IProductReader>(provider => cacheOptions.Enabled
            ? new CachedProductReader(provider.GetRequiredService<SqlProductReader>(),
                provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
                provider.GetRequiredService<IOptions<ProductCacheOptions>>(), provider.GetRequiredService<ProductCacheMetrics>())
            : provider.GetRequiredService<SqlProductReader>());

        services.AddScoped(provider => new CreateOrderHandler(
            provider.GetRequiredService<IOrderCreationStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<ReservationOptions>>().Value.Period));
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<GetProductHandler>();
        services.AddScoped<ProcessOrderPaymentHandler>();
        return services;
    }
}
