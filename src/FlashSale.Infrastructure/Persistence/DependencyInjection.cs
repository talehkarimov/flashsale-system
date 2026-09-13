using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Application.Orders.GetOrder;
using FlashSale.Application.Payments.ProcessOrderPayment;
using FlashSale.Infrastructure.Orders.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Persistence;

internal static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SaleDatabaseOptions>()
            .Bind(configuration.GetSection(SaleDatabaseOptions.SectionName))
            .Validate(database => database.IsValid(), "ConnectionStrings:FlashSale requires a SQL Server and database name.")
            .ValidateOnStart();
        services.AddDbContext<SaleDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<SaleDatabaseOptions>>().Value.FlashSale));
        services.AddScoped<IOrderCreationStore, SqlOrderCreationStore>();
        services.AddScoped<IOrderReader, SqlOrderReader>();
        services.AddScoped<IOrderPaymentCompletion, SqlOrderPaymentCompletion>();
        return services;
    }
}
