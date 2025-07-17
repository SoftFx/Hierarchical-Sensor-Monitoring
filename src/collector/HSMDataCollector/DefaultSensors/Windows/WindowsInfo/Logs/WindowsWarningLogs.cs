using System.Diagnostics;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsApplicationWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "Application";


        internal WindowsApplicationWarningLogs(InstantSensorOptions options) : base(options) { }
    }


    public sealed class WindowsSystemWarningLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Warning;

        protected override string Category => "System";


        internal WindowsSystemWarningLogs(InstantSensorOptions options) : base(options) { }
    }
}