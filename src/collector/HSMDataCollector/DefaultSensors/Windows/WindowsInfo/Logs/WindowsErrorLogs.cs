using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsErrorLogs : SensorBase<string>
    {
        private readonly DateTime _startTime = DateTime.UtcNow; // change to local now
        private readonly EventLog _eventLog;
        private readonly EventLogWatcher _eventLogWatcher;
        
        
        public WindowsErrorLogs(WindowsLogsOptions options) : base(options)
        {
            _eventLog = new EventLog("System", Environment.MachineName);
            _eventLogWatcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, "*[System[(Level=2)]]"));
            _eventLogWatcher.EventRecordWritten += Handler;
        }
        
        internal override Task<bool> Start()
        {
            foreach (var eventLog in from EventLogEntryCollection eventLogEntry in new List<object> { _eventLog.Entries } from EventLogEntry eventLog in eventLogEntry where eventLog.TimeGenerated >= _startTime && eventLog.EntryType is EventLogEntryType.Error select eventLog)
                SendValue(new StringSensorValue()
                {
                    Value = eventLog.Message,
                    Time = eventLog.TimeGenerated,
                    Status = SensorStatus.Ok,
                    Path = SensorPath
                });

            return base.Start();
        }
        
        
        private void Handler(object obj, EventRecordWrittenEventArgs arg)
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