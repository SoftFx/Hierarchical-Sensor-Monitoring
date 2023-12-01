using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;
using System.Text;

namespace HSMDataCollector.DefaultSensors.Windows.WindowsInfo
{
    internal sealed class WindowsVersion : MonitoringSensorBase<string>
    {
        private const string NotFoundError = "OS version information not found";

        private string _lastVersion;


        protected override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        public WindowsVersion(WindowsInfoSensorOptions options) : base(options) { }


        protected override string GetValue()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append(RegistryInfo.GetCurrentWindowsProductName())
              .Append($" {RegistryInfo.GetCurrentWindowsDisplayVersion()}")
              .Append($" ({RegistryInfo.GetCurrentWindowsFullBuildVersion()})");

            _lastVersion = sb.ToString();

            return _lastVersion.Trim() == "()" ? NotFoundError : _lastVersion;
        }

        protected override SensorStatus GetStatus() => string.IsNullOrEmpty(_lastVersion) ? SensorStatus.Error : SensorStatus.Ok;
    }
}