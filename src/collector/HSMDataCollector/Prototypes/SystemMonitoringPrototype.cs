namespace HSMDataCollector.Options
{
    internal sealed class SystemMonitoringPrototype : MonitoringPrototype<BarSensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";
    }
}
