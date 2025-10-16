using System;
using System.Management;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan, NoDisplayUnit>
    {
        internal WindowsLastRestart(MonitoringInstantSensorOptions options) : base(options) { }

        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastBootTime().ToUniversalTime();

        private DateTime GetLastBootTime()
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    string lastBootUpTime = mo["LastBootUpTime"].ToString();
                    return ManagementDateTimeConverter.ToDateTime(lastBootUpTime);
                }
            }

            throw new Exception("Can't get the date of the last reboot of Windows");
        }
    }
}
