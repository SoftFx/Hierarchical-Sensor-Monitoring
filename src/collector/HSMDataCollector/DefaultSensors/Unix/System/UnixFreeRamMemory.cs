using System;
using System.IO;
using System.Security;
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
            // A failed or unusable read is REPORTED (#1426) rather than silently thinning out this
            // sensor's bars — the native source answers both cases with HSM_METRIC_READ_SAMPLE_ERROR.
            var content = ReadMeminfo();
            if (content == null)
                return null;

            var availableKb = ProcMeminfo.ParseAvailableKb(content);
            if (!availableKb.HasValue)
            {
                HandleException(new InvalidDataException($"No usable memory fields in {ProcMeminfoPath}"));
                return null;
            }

            return availableKb.Value / KbPerMb;
        }

        private string ReadMeminfo()
        {
            try
            {
                return File.ReadAllText(ProcMeminfoPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                HandleException(ex);

                return null;
            }
        }
    }
}
