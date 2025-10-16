using System;
using System.Text;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Windows.WindowsInfo
{
    public sealed class WindowsVersion : MonitoringSensorBase<Version, NoDisplayUnit>
    {
        private Version _lastVersion;

        internal WindowsVersion(WindowsInfoSensorOptions options) : base(options) { }


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