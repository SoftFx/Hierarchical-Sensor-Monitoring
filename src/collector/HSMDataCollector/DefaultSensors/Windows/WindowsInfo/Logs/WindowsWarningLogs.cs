using System.Diagnostics;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsApplicationWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "Application";


        public WindowsApplicationWarningLogs(InstantSensorOptions options) : base(options) { }
    }


    public sealed class WindowsSystemWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "System";


        public WindowsSystemWarningLogs(InstantSensorOptions options) : base(options) { }
    }
}