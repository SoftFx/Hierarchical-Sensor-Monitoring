using System.Diagnostics;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsErrorLogs : WindowsLogsSensorBase
    {
        public WindowsErrorLogs(WindowsLogsOptions options) : base(options, EventLogEntryType.Error) { }
    }
}