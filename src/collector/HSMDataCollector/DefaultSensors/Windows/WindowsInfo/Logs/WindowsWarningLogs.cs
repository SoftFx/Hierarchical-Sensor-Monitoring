using System.Diagnostics;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsWarningLogs : WindowsLogsSensorBase
    {
        public WindowsWarningLogs(InstantSensorOptions options) : base(options, EventLogEntryType.Warning) { }
    }
}