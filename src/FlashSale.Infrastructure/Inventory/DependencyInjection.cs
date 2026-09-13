using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSale.Infrastructure.Inventory;

internal static class DependencyInjection
{
    public static IServiceCollection AddInventory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReservationOptions>()
            .Bind(configuration.GetSection(ReservationOptions.SectionName))
            .Validate(options => options.Period > TimeSpan.Zero && options.Period <= TimeSpan.FromDays(1),
                "Reservations:Period must be positive and no longer than one day.")
            .ValidateOnStart();
        services.AddScoped<SqlInventoryReservation>();
        return services;
    }
}
