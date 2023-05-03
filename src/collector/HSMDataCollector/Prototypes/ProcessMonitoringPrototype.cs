namespace HSMDataCollector.Options
{
    internal sealed class ProcessMonitoringPrototype : Prototype<BarSensorOptions>
    {
        protected override string NodePath { get; } = "Process monitoring";
    }
}
