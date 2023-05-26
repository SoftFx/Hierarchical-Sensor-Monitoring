namespace HSMDataCollector.Options
{
    internal sealed class WindowsInfoMonitoringPrototype : BarMonitoringPrototype<WindowsSensorOptions>
    {
        protected override string NodePath { get; } = "Windows OS Info";
    }
}