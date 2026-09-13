using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PaymentSimulator.Api;
using PaymentSimulator.Api.Configuration;

namespace FlashSale.IntegrationTests.Support;

public sealed class SimulatorFactory(SqlFixture sql, SimulatorScenario scenario = SimulatorScenario.Success) : WebApplicationFactory<SimulatorProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Payments", sql.PaymentConnection);
        builder.UseSetting("Simulator:Scenario", scenario.ToString());
        builder.UseSetting("Logging:LogLevel:Default", "Error");
    }
}
