using System;
using System.Linq;
using System.Management;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        private readonly string _lastBootTimeCommand = "((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalMilliseconds";
        
        public static string WMI_CLASS_NAME = "Win32_OperatingSystem";
        public static string PROPERTY_NAME  = "LastBootUpTime";

        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsLastRestart(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue() => TimeSpan.FromMilliseconds(GetLastBootTime());


        private long GetLastBootTime()
        {
            var mSeconds = 0L;
            using (var process = ProcessInfo.GetPowershellProcess(_lastBootTimeCommand))
            {
                process.Start();

                long.TryParse(process.StandardOutput.ReadToEnd(), out mSeconds);

                process.WaitForExit();
            }

            return mSeconds;
        }
    }
}
