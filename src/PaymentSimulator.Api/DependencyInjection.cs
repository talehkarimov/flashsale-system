using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentSimulator.Api.Configuration;
using PaymentSimulator.Api.Payments;
using PaymentSimulator.Api.Persistence;

namespace PaymentSimulator.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddPaymentSimulator(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentDatabaseOptions>()
            .Bind(configuration.GetSection(PaymentDatabaseOptions.SectionName))
            .Validate(database => database.IsValid(), "ConnectionStrings:Payments requires a SQL Server and database name.")
            .ValidateOnStart();
        services.AddDbContext<PaymentDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<PaymentDatabaseOptions>>().Value.Payments));
        services.AddOptions<SimulatorOptions>()
            .Bind(configuration.GetSection(SimulatorOptions.SectionName))
            .Validate(options => Enum.IsDefined(options.Scenario), "Simulator:Scenario is invalid.")
            .Validate(options => options.Delay >= TimeSpan.Zero && options.Delay <= SimulatorOptions.MaximumDelay,
                "Simulator:Delay must be between zero and 30 seconds.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SqlPaymentLedger>();
        services.AddScoped<PaymentProcessor>();
        return services;
    }
}
