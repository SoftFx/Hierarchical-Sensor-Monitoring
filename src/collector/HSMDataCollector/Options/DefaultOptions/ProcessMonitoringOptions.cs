namespace HSMDataCollector.Options
{
    internal sealed class ProcessMonitoringOptions : OptionsProperty<BarSensorOptions>
    {
        protected override string NodePath { get; } = "Process monitoring";
    }
}
