using FlashSale.Application.Payments.Contracts;
using FlashSale.Infrastructure.Payments.Http;
using FlashSale.Infrastructure.Payments.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Payments;

public static class DependencyInjection
{
    public static IHttpClientBuilder AddPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<PaymentOptions>, PaymentOptionsValidator>();
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .ValidateOnStart();
        services.AddTransient<PaymentResponseBufferingHandler>();
        var configuredPayment = new PaymentOptions();
        configuration.GetSection(PaymentOptions.SectionName).Bind(configuredPayment);

        var client = services.AddHttpClient<IPaymentGateway, PaymentGatewayHttpClient>((provider, http) =>
        {
            http.BaseAddress = provider.GetRequiredService<IOptions<PaymentOptions>>().Value.BaseAddress;
            http.Timeout = Timeout.InfiniteTimeSpan;
        });
        client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
        client.AddPaymentResilience(configuredPayment.Resilience);
        client.AddHttpMessageHandler<PaymentResponseBufferingHandler>();
        return client;
    }
}
