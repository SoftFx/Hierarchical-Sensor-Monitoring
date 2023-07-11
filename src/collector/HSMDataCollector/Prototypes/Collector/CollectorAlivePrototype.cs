namespace HSMDataCollector.Options
{
    internal sealed class CollectorAlivePrototype : MonitoringPrototype<CollectorMonitoringInfoOptions>
    {
        protected override string NodePath { get; } = CollectorInfoOptions.BaseCollectorPath;
    }
}