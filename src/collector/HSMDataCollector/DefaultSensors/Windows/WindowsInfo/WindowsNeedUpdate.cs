using HSMDataCollector.Options;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsNeedUpdate : MonitoringSensorBase<bool>
    {
        private readonly TimeSpan _acceptableUpdateInterval = TimeSpan.FromDays(31);
        private readonly DateTime _windowsLastUpdate;

        
        public WindowsNeedUpdate(WindowsInfoSensorOptions options) : base(options)
        {
            _windowsLastUpdate = RegistryInfo.GetInstallationDate();
        }


        internal override Task<bool> Start() //send data on start
        {
            OnTimerTick();

            return base.Start();
        }

        protected override bool GetValue() => DateTime.UtcNow - _windowsLastUpdate >= _acceptableUpdateInterval;

        protected override string GetComment() => $"{RuntimeInformation.OSDescription}. Last update date: {_windowsLastUpdate}";
    }
}
