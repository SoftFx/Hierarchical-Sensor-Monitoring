namespace HSMDataCollector.Options
{
    internal sealed class ProcessMonitoringPrototype : MonitoringPrototype<BarSensorOptions>
    {
        protected override string NodePath { get; } = "Process monitoring";
    }
}
