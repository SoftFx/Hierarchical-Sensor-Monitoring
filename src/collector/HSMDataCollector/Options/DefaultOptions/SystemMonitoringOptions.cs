namespace HSMDataCollector.Options
{
    internal sealed class SystemMonitoringOptions : OptionsProperty<BarSensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";
    }
}
