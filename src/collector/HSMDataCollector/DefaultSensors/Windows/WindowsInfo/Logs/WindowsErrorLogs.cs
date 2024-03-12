using System.Diagnostics;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsApplicationErrorLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Error;

        protected override string Category => "System";


        public WindowsApplicationErrorLogs(InstantSensorOptions options) : base(options) { }
    }


    public sealed class WindowsSystemErrorLogs : WindowsLogsSensorBase
    {
        protected override EventLogEntryType LogType => EventLogEntryType.Error;

        protected override string Category => "System";


        public WindowsSystemErrorLogs(InstantSensorOptions options) : base(options) { }
    }
}