using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Text;

namespace HSMDataCollector.DefaultSensors.Windows.WindowsInfo
{
    internal sealed class WindowsVersion : MonitoringSensorBase<Version>
    {
        private Version _lastVersion;


        protected override TimeSpan TimerDueTime => PostTimePeriod.GetTimerDueTime();


        public WindowsVersion(WindowsInfoSensorOptions options) : base(options) { }


        protected override Version GetValue()
        {
            _lastVersion = RegistryInfo.GetCurrentWindowsFullBuildVersion();

            return _lastVersion;
        }

        protected override string GetComment()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append(RegistryInfo.GetCurrentWindowsProductName())
              .Append($" {RegistryInfo.GetCurrentWindowsDisplayVersion()}")
              .Append($" ({_lastVersion.Major}.{_lastVersion.Minor}.{_lastVersion.Build})");

            return sb.ToString();
        }
    }
}