using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private readonly DateTime _lastUpdateDate;

        protected override TimeSpan TimerDueTime => TimeSpan.FromSeconds(10);//_receiveDataPeriod.GetTimerDueTime();


        public WindowsLastUpdate(WindowsInfoSensorOptions options) : base(options)
        {
            _lastUpdateDate = RegistryInfo.GetInstallationDate();
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - _lastUpdateDate;
    }
}
