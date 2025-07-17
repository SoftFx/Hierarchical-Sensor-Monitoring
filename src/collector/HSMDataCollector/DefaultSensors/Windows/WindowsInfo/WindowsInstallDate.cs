using System;
using System.Management;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsInstallDate : MonitoringSensorBase<TimeSpan>
    {

        internal WindowsInstallDate(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetWindowsInstallDate().ToUniversalTime();


        public static DateTime GetWindowsInstallDate()
        {

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem"))
            using (ManagementObjectCollection result = searcher.Get())
            {
                foreach (ManagementObject os in result)
                {
                    if (os["InstallDate"] != null)
                    {
                        string installDateString = os["InstallDate"].ToString();
                        return ManagementDateTimeConverter.ToDateTime(installDateString);
                    }
                }
            }

            throw new Exception("Install date not found");
        }
    }
}
