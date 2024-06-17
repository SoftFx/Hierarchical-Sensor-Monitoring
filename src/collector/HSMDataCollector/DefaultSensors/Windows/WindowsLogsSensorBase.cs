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
        private readonly EventLog _eventLog;


        protected abstract EventLogEntryType LogType { get; }

        protected abstract string Category { get; }


        protected WindowsLogsSensorBase(SensorOptions options) : base(options)
        {
            _eventLog = new EventLog(Category, Environment.MachineName);

            _eventLogWatcher = new EventLogWatcher(new EventLogQuery(Category, PathType.LogName, $"*[System[({GetCurrentLogLevel()})]]"))
            {
                Enabled = true,
            };

            _eventLogWatcher.EventRecordWritten += Handler;
        }


        internal override Task<bool> StartAsync()
        {
            try
            {
                foreach (EventLogEntryCollection eventLogEntry in new List<object> { _eventLog.Entries })
                    foreach (var eventLog in eventLogEntry.Cast<EventLogEntry>())
                        if (eventLog.TimeGenerated.ToUniversalTime() >= _startTime && eventLog.EntryType == LogType)
                            SendValue(BuildRecordValue(eventLog.InstanceId.ToString(), eventLog.TimeGenerated, eventLog.Source, eventLog.Message));
            }
            catch (Exception exception)
            {
                ThrowException(exception);
            }

            return base.StartAsync();
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
            switch (LogType)
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