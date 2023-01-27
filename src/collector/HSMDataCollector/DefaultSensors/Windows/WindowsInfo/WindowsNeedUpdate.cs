using HSMDataCollector.Options;
using System;
using System.Runtime.InteropServices;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsNeedUpdate : MonitoringSensorBase<bool>
    {
        private readonly string _windowsVersion = RuntimeInformation.OSDescription;
        private readonly DateTime _windowsLastUpdate;
        private readonly TimeSpan _acceptableUpdateInterval;


        protected override string SensorName => "Is need update";


        public WindowsNeedUpdate(WindowsSensorOptions options) : base(options)
        {
            _acceptableUpdateInterval = options.AcceptableUpdateInterval;

            _windowsLastUpdate = RegistryInfo.GetInstallationDate();
        }


        protected override bool GetValue() => DateTime.UtcNow - _windowsLastUpdate >= _acceptableUpdateInterval;

        protected override string GetComment() => $"{_windowsVersion}. Last update date: {_windowsLastUpdate}";
    }
}
