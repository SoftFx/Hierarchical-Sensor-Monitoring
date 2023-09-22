using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsWarningLogs : WindowsLogsSensorBase
    {
        public WindowsWarningLogs(WindowsLogsOptions options) : base(options, EventLogEntryType.Warning)
        {
            _eventLogWatcher.EventRecordWritten += Handler;
        }
 
        protected override void Handler(object obj, EventRecordWrittenEventArgs arg)
        {
            if (arg.EventRecord != null)
            {
                SendValue(arg.EventRecord.FormatDescription()); 
            }
            else
            {
                Console.WriteLine("The event instance was null.");
            }
        }
    }
}