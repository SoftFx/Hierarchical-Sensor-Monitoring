using System;
using System.Diagnostics.Eventing.Reader;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private const int INSTALLATION_SUCCESS_CODE = 2;

        private readonly EventLogQuery _query = new EventLogQuery("SetUp", PathType.LogName) { ReverseDirection = true };

        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsLastUpdate(SensorOptions options) : base(options) { }


        private DateTime GetLastSuccessfulUpdateTime()
        {
            using (EventLogReader reader = new EventLogReader(_query))
            {
                EventRecord eventRecord;
                while ((eventRecord = reader.ReadEvent()) != null)
                {
                    if (eventRecord.Id == INSTALLATION_SUCCESS_CODE)
                        return eventRecord.TimeCreated.Value;
                }
            }

            return RegistryInfo.GetInstallationDate();
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastSuccessfulUpdateTime().ToUniversalTime();
    }
}