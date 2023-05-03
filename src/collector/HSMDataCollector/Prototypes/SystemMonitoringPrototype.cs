namespace HSMDataCollector.Options
{
    internal sealed class SystemMonitoringPrototype : Prototype<BarSensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";
    }
}
