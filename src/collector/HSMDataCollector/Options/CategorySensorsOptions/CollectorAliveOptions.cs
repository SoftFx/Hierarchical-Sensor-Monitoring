namespace HSMDataCollector.Options
{
    internal sealed class CollectorAliveOptions : OptionsProperty<SensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";
    }
}
