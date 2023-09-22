using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsErrorLogs : WindowsLogsSensorBase
    {
        public WindowsErrorLogs(WindowsLogsOptions options) : base(options, EventLogEntryType.Error)
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