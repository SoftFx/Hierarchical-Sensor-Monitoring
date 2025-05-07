using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        protected override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        public WindowsLastUpdate(SensorOptions options) : base(options) { }

        internal override ValueTask<bool> StartAsync()
        {
            SendValueAction();
            return base.StartAsync();
        }

        private DateTime GetLastSuccessfulUpdateTime()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_QuickFixEngineering"))
            using (var results = searcher.Get())
            {
                var lastUpdate = results.Cast<ManagementObject>()
                    .Select(x => DateTime.Parse(x["InstalledOn"].ToString()))
                    .OrderByDescending(x => x)
                    .FirstOrDefault();

                return lastUpdate;
            }
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastSuccessfulUpdateTime().ToUniversalTime();
    }
}