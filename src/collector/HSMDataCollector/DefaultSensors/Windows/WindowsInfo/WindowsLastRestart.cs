using System;
using System.Management;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsLastRestart(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastBootTime().ToUniversalTime();

        internal override ValueTask<bool> StartAsync()
        {
            SendValueAction();
            return base.StartAsync();
        }

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
