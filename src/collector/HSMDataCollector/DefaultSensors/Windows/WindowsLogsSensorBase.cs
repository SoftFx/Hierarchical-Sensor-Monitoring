using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsLogsSensorBase : SensorBase<string, NoDisplayUnit>
    {
        private EventLog _eventLog;

        protected abstract EventLogEntryType LogType { get; }

        protected abstract string Category { get; }


        protected WindowsLogsSensorBase(SensorOptions<NoDisplayUnit> options) : base(options)
        {
        }


        public override ValueTask<bool> StartAsync()
        {
            try
            {
                _eventLog = new EventLog(Category, Environment.MachineName)
                {
                    EnableRaisingEvents = true,
                };

                _eventLog.EntryWritten += Handler;
            }
            catch (Exception exception)
            {
                HandleException(exception);

                return new ValueTask<bool>(false);
            }

            return base.StartAsync();
        }

        public override ValueTask StopAsync()
        {
            if (_eventLog != null)
            {
                _eventLog.EntryWritten -= Handler;
                _eventLog.Dispose();
            }

            return base.StopAsync();
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

        private void Handler(object obj, EntryWrittenEventArgs arg)
        {
            try
            {
                var record = arg.Entry;

                if (record != null && record.EntryType == LogType)
                    SendValue(BuildRecordValue(record.InstanceId.ToString(), record.TimeGenerated, record.Source, record.Message));

            }
            catch (Exception exception)
            {
                HandleException(exception);
            }
        }

    }
}