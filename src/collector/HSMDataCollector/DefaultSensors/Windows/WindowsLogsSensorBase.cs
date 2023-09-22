using System;
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
    public abstract class WindowsLogsSensorBase : SensorBase<string>
    {
        private readonly EventLogEntryType _eventType;
        private readonly DateTime _startTime = DateTime.Now;
        private readonly EventLog _eventLog;
        
        protected readonly EventLogWatcher _eventLogWatcher;
        
        protected WindowsLogsSensorBase(SensorOptions options, EventLogEntryType type) : base(options)
        {
            _eventType = type;
            _eventLog = new EventLog("System", Environment.MachineName);
            _eventLogWatcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, $"*[System[({GetCurrentLogLevel()})]]"));
            _eventLogWatcher.EventRecordWritten += Handler;
        }

        private string GetCurrentLogLevel() => _eventType is EventLogEntryType.Error ? "Level 2" : "Level 3";

        protected abstract void Handler(object obj, EventRecordWrittenEventArgs arg);
        
        internal override Task<bool> Start()
        {
            foreach (var eventLog in from EventLogEntryCollection eventLogEntry in new List<object> { _eventLog.Entries } from EventLogEntry eventLog in eventLogEntry where eventLog.TimeGenerated >= _startTime && eventLog.EntryType == _eventType select eventLog)
                SendValue(new StringSensorValue()
                {
                    Value = eventLog.Message,
                    Time = eventLog.TimeGenerated,
                    Status = SensorStatus.Ok,
                    Path = SensorPath,
                    Comment = eventLog.Source
                });

            return base.Start();
        }
    }
}