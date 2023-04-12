namespace HSMDataCollector.Options
{
    internal sealed class CollectorAliveOptions : OptionsProperty<MonitoringSensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";
    }
}
