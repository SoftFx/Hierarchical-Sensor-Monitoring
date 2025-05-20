using System;
using System.Globalization;
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
                var lastUpdate = results.OfType<ManagementObject>()
                    .Select(x => DateTime.TryParse(x["InstalledOn"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTime?) null)
                    .Where(date => date.HasValue)
                    .Max();

                return lastUpdate ?? throw new Exception("Can't get the date of the last update of Windows"); ;
            }
        }


        protected override TimeSpan GetValue() => DateTime.UtcNow - GetLastSuccessfulUpdateTime().ToUniversalTime();
    }
}