using System.Diagnostics;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsErrorLogs : WindowsLogsSensorBase
    {
        public WindowsErrorLogs(InstantSensorOptions options) : base(options, EventLogEntryType.Error) { }
    }
}