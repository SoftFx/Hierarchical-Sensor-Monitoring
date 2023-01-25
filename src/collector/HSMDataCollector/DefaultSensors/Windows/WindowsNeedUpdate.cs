using HSMDataCollector.Helpers;
using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsNeedUpdate : MonitoringSensorBase<bool>
    {
        private string WindowsVersion { get; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

        private DateTime WindowsLastUpdate { get; }

        private TimeSpan AcceptableUpdateInterval { get; }


        protected override string SensorName => "Is need Windows update";


        public WindowsNeedUpdate(WindowsSensorOptions options) : base(options)
        {
            AcceptableUpdateInterval = options.AcceptableUpdateInterval;

            WindowsLastUpdate = WindowsInfo.GetInstallationDate();
        }


        protected override bool GetValue() => DateTime.UtcNow - WindowsLastUpdate >= AcceptableUpdateInterval;

        protected override string GetComment() => $"{WindowsVersion}. Last update date: {WindowsLastUpdate}";
    }
}
