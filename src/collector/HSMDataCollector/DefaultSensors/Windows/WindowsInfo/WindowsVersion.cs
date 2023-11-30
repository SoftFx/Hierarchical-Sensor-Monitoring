using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;
using System.Text;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows.WindowsInfo
{
    internal sealed class WindowsVersion : MonitoringSensorBase<string>
    {
        private const string NotFindError = "OS version information not found";

        protected override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();

        private string _lastVersion;


        public WindowsVersion(WindowsInfoSensorOptions options) : base(options) { }


        protected override string GetValue()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append(RegistryInfo.GetCurrentWindowsProductName())
              .Append(" ")
              .Append(RegistryInfo.GetCurrentWindowsDisplayVersion())
              .Append($" ({RegistryInfo.GetCurrentWindowsFullBuildVersion()})");

            _lastVersion = sb.ToString();

            return _lastVersion.Trim() == "()" ? NotFindError : _lastVersion;
        }

        protected override SensorStatus GetStatus() => string.IsNullOrEmpty(_lastVersion) ? SensorStatus.Error : SensorStatus.Ok;
    }
}