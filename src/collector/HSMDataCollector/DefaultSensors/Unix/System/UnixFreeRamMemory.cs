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
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // No /proc on this host at all (macOS/FreeBSD — UnixSensorsCollection is chosen for
                // every non-Windows OS — or /proc masked in a container). A platform fact, not data
                // loss: the sensor can never produce a value here, and the native collector is
                // likewise silent because its Linux factory is compiled out. See UnixTotalCpu.
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                // The file EXISTS but could not be read: a real failure on a host where this sensor
                // is supposed to work.
                HandleException(ex);

                return null;
            }
        }
    }
}
