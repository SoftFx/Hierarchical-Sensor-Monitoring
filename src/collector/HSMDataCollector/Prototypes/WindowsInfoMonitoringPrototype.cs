namespace HSMDataCollector.Options
{
    internal sealed class WindowsInfoMonitoringPrototype : MonitoringPrototype<WindowsSensorOptions>
    {
        protected override string NodePath { get; } = "Windows OS Info";
    }
}