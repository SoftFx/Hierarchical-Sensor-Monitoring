using HSMDataCollector.Options;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsApplicationWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "Application";


        public WindowsApplicationWarningLogs(InstantSensorOptions options) : base(options) { }
    }


    public class WindowsSystemWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "System";


        public WindowsSystemWarningLogs(InstantSensorOptions options) : base(options) { }
    }
}