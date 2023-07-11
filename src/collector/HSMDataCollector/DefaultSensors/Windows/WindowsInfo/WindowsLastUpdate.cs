using HSMDataCollector.Options;
using System;
using System.Threading.Tasks;

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


        internal override Task<bool> Start() //send data on start
        {
            OnTimerTick();

            return base.Start();
        }

        protected override TimeSpan GetValue() => DateTime.UtcNow - _lastUpdateDate;
    }
}
