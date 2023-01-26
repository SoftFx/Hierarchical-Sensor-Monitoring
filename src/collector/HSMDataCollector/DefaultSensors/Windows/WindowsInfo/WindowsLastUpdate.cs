using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        private DateTime LastUpdateDate { get; }

        protected override string SensorName => "Windows last update";


        public WindowsLastUpdate(SensorOptions options) : base(options)
        {
            LastUpdateDate = RegistryInfo.GetInstallationDate();
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - LastUpdateDate;
    }
}
