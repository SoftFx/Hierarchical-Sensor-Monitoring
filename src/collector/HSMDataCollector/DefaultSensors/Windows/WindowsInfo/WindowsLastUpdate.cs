using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Diagnostics.Eventing.Reader;


namespace HSMDataCollector.DefaultSensors.Windows
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private const int InstallationSuccessCode = 2;

        private readonly EventLogQuery _query = new EventLogQuery("SetUp", PathType.LogName) { ReverseDirection = true };

        protected override TimeSpan TimerDueTime => PostTimePeriod.GetTimerDueTime();


        public WindowsLastUpdate(SensorOptions options) : base(options) { }


        public DateTime GetLastSuccessfulUpdateTime()
        {
            using (EventLogReader reader = new EventLogReader(_query))
            {
                EventRecord eventRecord;
                while ((eventRecord = reader.ReadEvent()) != null)
                {
                    if (eventRecord.Id == InstallationSuccessCode)
                        return eventRecord.TimeCreated.Value;
                }
            }

            return RegistryInfo.GetInstallationDate();
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastSuccessfulUpdateTime().ToUniversalTime();
    }
}