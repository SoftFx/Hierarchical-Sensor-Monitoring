using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private readonly DateTime _lastUpdateDate;

        protected override string SensorName => "Last update";


        public WindowsLastUpdate(MonitoringSensorOptions options) : base(options)
        {
            _lastUpdateDate = RegistryInfo.GetInstallationDate();
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - _lastUpdateDate;
    }
}
