using HSMDataCollector.Helpers;
using System;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsNeedUpdate : MonitoringSensorBase<bool>
    {
        private readonly TimeSpan _defaultReceivedDataPeriod = TimeSpan.FromHours(24);
        private readonly TimeSpan _defaultExpectedUpdateInterval = TimeSpan.FromDays(30);


        private string WindowsVersion { get; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

        private DateTime WindowsLastUpdate { get; }

        private TimeSpan ExpectedUpdateInterval { get; }


        protected override string SensorName => "Is need Windows update";


        public WindowsNeedUpdate(string nodePath, TimeSpan? receivedDataPeriod, TimeSpan? updateInterval)
            : base(nodePath)
        {
            ReceiveDataPeriod = receivedDataPeriod ?? _defaultReceivedDataPeriod;
            ExpectedUpdateInterval = updateInterval ?? _defaultExpectedUpdateInterval;

            WindowsLastUpdate = WindowsInfo.GetInstallationDate();
        }


        protected override bool GetValue() => DateTime.UtcNow - WindowsLastUpdate >= ExpectedUpdateInterval;

        protected override string GetComment() => $"{WindowsVersion}. Last update date: {WindowsLastUpdate}";
    }
}
