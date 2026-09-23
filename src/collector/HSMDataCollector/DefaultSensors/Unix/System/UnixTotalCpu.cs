using System;
using System.IO;
using System.Security;
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
            _cpuUsage = new ProcStatCpuUsage(ReadProcStat(reportFailure: false));
        }


        protected override double? GetBarData()
        {
            // A failed or unusable read is REPORTED (#1426): it reaches the collector's error
            // channel — the deduplicated log and `.module/Collector errors` — instead of silently
            // thinning out this sensor's bars. The bar still gets no sample either way, matching the
            // native source, which answers the same two cases with HSM_METRIC_READ_SAMPLE_ERROR.
            var content = ReadProcStat(reportFailure: true);
            if (content == null)
                return null;

            if (ProcStat.ParseCpuTimes(content) == null)
            {
                HandleException(new InvalidDataException($"Unexpected content in {ProcStatPath}"));
                return null;
            }

            // A null from here is a legitimately empty tick (no baseline yet, no elapsed jiffies),
            // which the native source answers with NO_VALUE and neither collector reports.
            return _cpuUsage.NextBusyPercent(content);
        }

        private string ReadProcStat(bool reportFailure)
        {
            try
            {
                return File.ReadAllText(ProcStatPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // There is no /proc on this host at all — UnixSensorsCollection is chosen for EVERY
                // non-Windows OS, so this is macOS/FreeBSD, or a container with /proc masked. The
                // sensor can never produce a value there; that is a platform fact, not data loss, and
                // reporting it every tick forever would be noise. Stays silent, which is also what the
                // native collector does on such a host (its Linux factory is compiled out, so the
                // sensor is simply registration-only) — reporting here would recreate the very
                // divergence #1426 removed, in the opposite direction.
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                // /proc/stat EXISTS but could not be read (permissions, I/O error): a real failure on
                // a host where this sensor is supposed to work, so it is reported. The constructor's
                // baseline read stays silent — it runs before the sensor is started, and every later
                // tick reports the same failure anyway.
                if (reportFailure)
                    HandleException(ex);

                return null;
            }
        }
    }
}
