using System;
using System.IO;
using HSMDataCollector.DefaultSensors.Unix.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    public sealed class UnixFreeRamMemory : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string ProcMeminfoPath = "/proc/meminfo";
        private const double KbPerMb = 1024.0;


        internal UnixFreeRamMemory(BarSensorOptions options) : base(options) { }


        protected override double? GetBarData()
        {
            var availableKb = ProcMeminfo.ParseAvailableKb(ReadMeminfo());

            return availableKb.HasValue ? availableKb.Value / KbPerMb : (double?)null;
        }

        private static string ReadMeminfo()
        {
            try
            {
                return File.ReadAllText(ProcMeminfoPath);
            }
            catch
            {
                return null;
            }
        }
    }
}
