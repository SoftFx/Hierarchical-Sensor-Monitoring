using System;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsLastUpdate : MonitoringSensorBase<TimeSpan>
    {
        internal WindowsLastUpdate(SensorOptions options) : base(options) { }

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