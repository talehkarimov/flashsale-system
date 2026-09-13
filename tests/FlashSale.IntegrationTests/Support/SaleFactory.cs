using FlashSale.Application.Payments.Contracts;
using FlashSale.Infrastructure.Outbox.Processing;
using FlashSale.Infrastructure.Payments.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlashSale.IntegrationTests.Support;

public sealed class SaleFactory(SqlFixture sql, SimulatorFactory simulator, TimeProvider? clock = null,
    Action<IServiceCollection>? configure = null, bool workerEnabled = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FlashSale", sql.SaleConnection);
        builder.UseSetting("Outbox:WorkerEnabled", workerEnabled.ToString());
        builder.UseSetting("Logging:LogLevel:Default", "Error");
        builder.UseSetting("Logging:LogLevel:FlashSale.Infrastructure.Outbox.Processing.PaymentOutboxProcessor", "Warning");
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<IPaymentGateway, PaymentGatewayHttpClient>().ConfigurePrimaryHttpMessageHandler(simulator.Server.CreateHandler);
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
            configure?.Invoke(services);
        });
    }
}
