using System;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        public const string LastBootTimeCommand = "((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime).TotalMilliseconds";

        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsLastRestart(WindowsInfoSensorOptions options) : base(options) { }


        protected override TimeSpan GetValue() => TimeSpan.FromMilliseconds(GetLastBootTime());


        private long GetLastBootTime()
        {
            var mSeconds = 0L;
            using (var process = ProcessInfo.GetPowershellProcess(LastBootTimeCommand))
            {
                process.Start();

                double.TryParse(process.StandardOutput.ReadToEnd(), out var dValue);
                mSeconds = (long)dValue;

                process.WaitForExit();
            }

            return mSeconds;
        }
    }
}
