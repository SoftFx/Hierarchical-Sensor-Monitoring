using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsLogsSensorBase : SensorBase<string>
    {
        private readonly DateTime _startTime = DateTime.UtcNow;
        private readonly EventLogWatcher _eventLogWatcher;
        private readonly EventLogEntryType _eventType;
        private readonly EventLog _eventLog;


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
            try
            {
                foreach (EventLogEntryCollection eventLogEntry in new List<object> { _eventLog.Entries })
                    foreach (var eventLog in eventLogEntry.Cast<EventLogEntry>())
                        if (eventLog.TimeGenerated.ToUniversalTime() >= _startTime && eventLog.EntryType == _eventType)
                            SendValue(BuildRecordValue(eventLog.InstanceId.ToString(), eventLog.TimeGenerated, eventLog.Source, eventLog.Message));
            }
            catch (Exception exception)
            {
                ThrowException(exception);
            }

            return base.Start();
        }

        private StringSensorValue BuildRecordValue(string eventId, DateTime time, string source, string message) =>
            new StringSensorValue()
            {
                Value = eventId,
                Time = time.ToUniversalTime(),
                Status = SensorStatus.Ok,
                Path = SensorPath,
                Comment = $"Source: {source}. Message: {message}"
            };

        private void Handler(object obj, EventRecordWrittenEventArgs arg)
        {
            try
            {
                var record = arg.EventRecord;

                if (record != null)
                    SendValue(BuildRecordValue(record.Id.ToString(), record.TimeCreated ?? DateTime.Now, record.ProviderName, record.FormatDescription()));
            }
            catch (Exception exception)
            {
                ThrowException(exception);
            }
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