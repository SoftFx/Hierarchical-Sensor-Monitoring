using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Linq;
using System.Management;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        public static string WMI_CLASS_NAME = "Win32_OperatingSystem";
        public static string PROPERTY_NAME  = "LastBootUpTime";

        protected override TimeSpan TimerDueTime => PostTimePeriod.GetTimerDueTime();


        public WindowsLastRestart(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastBootTime();


        private DateTime GetLastBootTime()
        {
            using (var searcher = new ManagementObjectSearcher($"SELECT {PROPERTY_NAME} FROM {WMI_CLASS_NAME}"))
            {
                var wmiObject = searcher.Get().OfType<ManagementObject>().FirstOrDefault();

                if (wmiObject != null)
                {
                    return ManagementDateTimeConverter.ToDateTime(wmiObject.Properties[PROPERTY_NAME].Value.ToString()).ToUniversalTime();
                }
            }

            return DateTime.MinValue;
        }
    }
}
