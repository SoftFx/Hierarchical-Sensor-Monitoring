namespace HSMDataCollector.Options
{
    internal sealed class CollectorAlivePrototype : Prototype<CollectorMonitoringInfoOptions>
    {
        protected override string NodePath { get; } = CollectorInfoOptions.BaseCollectorPath;
    }
}