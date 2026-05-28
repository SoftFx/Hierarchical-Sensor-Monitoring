using System;
using System.IO;
using HSMDataCollector.DefaultSensors.Unix.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    public sealed class UnixTotalCpu : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const string ProcStatPath = "/proc/stat";

        private readonly ProcStatCpuUsage _cpuUsage;


        internal UnixTotalCpu(BarSensorOptions options) : base(options)
        {
            // Seed the baseline so the first collected bar measures usage since construction,
            // not since boot.
            _cpuUsage = new ProcStatCpuUsage(ReadProcStat());
        }


        protected override double? GetBarData() => _cpuUsage.NextBusyPercent(ReadProcStat());

        private static string ReadProcStat()
        {
            try
            {
                return File.ReadAllText(ProcStatPath);
            }
            catch
            {
                // /proc/stat unavailable (non-Linux host, sandbox) — the parser turns null into a
                // skipped bar rather than a fault.
                return null;
            }
        }
    }
}
