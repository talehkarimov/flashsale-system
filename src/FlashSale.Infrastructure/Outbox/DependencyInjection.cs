using FlashSale.Infrastructure.Outbox.Persistence;
using FlashSale.Infrastructure.Outbox.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Outbox;

internal static class DependencyInjection
{
    public static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<SqlPaymentOutboxStore>();
        services.AddScoped<PaymentOutboxProcessor>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}
