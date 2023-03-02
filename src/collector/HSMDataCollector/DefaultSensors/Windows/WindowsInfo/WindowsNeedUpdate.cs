using HSMDataCollector.Options;
using System;
using System.Runtime.InteropServices;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsNeedUpdate : MonitoringSensorBase<bool>
    {
        private readonly DateTime _windowsLastUpdate;
        private readonly TimeSpan _acceptableUpdateInterval;


        protected override string SensorName => "Is need update";


        public WindowsNeedUpdate(WindowsSensorOptions options) : base(options)
        {
            _acceptableUpdateInterval = options.AcceptableUpdateInterval;

            _windowsLastUpdate = RegistryInfo.GetInstallationDate();
        }


        protected override bool GetValue() => DateTime.UtcNow - _windowsLastUpdate >= _acceptableUpdateInterval;

        protected override string GetComment() => $"{RuntimeInformation.OSDescription}. Last update date: {_windowsLastUpdate}";
    }
}
