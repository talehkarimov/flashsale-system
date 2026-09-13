namespace PaymentSimulator.Api.Configuration;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    public SimulatorScenario Scenario { get; set; } = SimulatorScenario.Success;
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(5);
}
