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
        private readonly DateTime _startTime = DateTime.Now;
        private readonly EventLogEntryType _eventType;
        private readonly EventLog _eventLog;
        private readonly EventLogWatcher _eventLogWatcher;


        protected WindowsLogsSensorBase(SensorOptions options, EventLogEntryType type) : base(options)
        {
            _eventType = type;
            _eventLog = new EventLog("System", Environment.MachineName);
            _eventLogWatcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, $"*[System[({GetCurrentLogLevel()})]]"))
            {
                Enabled = true
            };
            _eventLogWatcher.EventRecordWritten += Handler;
        }


        internal override Task<bool> Start()
        {
            foreach (EventLogEntryCollection eventLogEntry in new List<object> { _eventLog.Entries })
                foreach (var eventLog in eventLogEntry.Cast<EventLogEntry>())
                    if (eventLog.TimeGenerated >= _startTime && eventLog.EntryType == _eventType)
                        SendValue(BuildValue(eventLog.Message, eventLog.TimeGenerated, eventLog.Source));

            return base.Start();
        }

        private StringSensorValue BuildValue(string value, DateTime time, string source) => new StringSensorValue()
            {
                Value = value,
                Time = time,
                Status = SensorStatus.Ok,
                Path = SensorPath,
                Comment = source
            };

        private void Handler(object obj, EventRecordWrittenEventArgs arg)
        {
            if (arg.EventRecord != null)
                SendValue(BuildValue(arg.EventRecord.FormatDescription(), arg.EventRecord.TimeCreated ?? DateTime.Now, arg.EventRecord.ProviderName)); 
        }

        private string GetCurrentLogLevel()
        {
            switch (_eventType)
            {
                case EventLogEntryType.Error:
                    return "Level=2";
                case EventLogEntryType.Warning:
                    return "Level=3";
                default:
                    return string.Empty;
            }
        }
    }
}