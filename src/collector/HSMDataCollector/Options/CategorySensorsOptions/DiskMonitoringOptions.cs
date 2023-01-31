namespace HSMDataCollector.Options
{
    internal sealed class DiskMonitoringOptions : OptionsProperty<DiskSensorOptions>
    {
        protected override string NodePath { get; } = "Disk monitoring";
    }
}
