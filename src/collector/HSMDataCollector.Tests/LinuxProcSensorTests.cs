using HSMDataCollector.DefaultSensors.Unix.SystemInfo;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class LinuxProcSensorTests
    {
        private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        [Fact]
        public void ProcStat_cpu_busy_percent_is_in_range()
        {
            if (!OnLinux)
                return;

            var usage = new ProcStatCpuUsage(File.ReadAllText("/proc/stat"));

            // Burn a little CPU so the inter-sample delta is meaningful.
            var stopwatch = Stopwatch.StartNew();
            double sink = 0;
            while (stopwatch.ElapsedMilliseconds < 300)
                sink += Math.Sqrt(stopwatch.ElapsedTicks);
            Thread.Sleep(50);

            var busy = usage.NextBusyPercent(File.ReadAllText("/proc/stat"));

            Assert.True(busy.HasValue, "Expected a CPU busy value from /proc/stat on Linux.");
            Assert.InRange(busy.Value, 0.0, 100.0);
            GC.KeepAlive(sink);
        }

        [Fact]
        public void ProcMeminfo_available_is_positive()
        {
            if (!OnLinux)
                return;

            var availableKb = ProcMeminfo.ParseAvailableKb(File.ReadAllText("/proc/meminfo"));

            Assert.True(availableKb.HasValue, "Expected MemAvailable from /proc/meminfo on Linux.");
            Assert.True(availableKb.Value > 0, "MemAvailable should be positive on a running Linux host.");
        }

        [Fact]
        public void DriveInfo_root_free_space_is_positive()
        {
            if (!OnLinux)
                return;

            var freeKb = new DriveInfo("/").AvailableFreeSpace / 1024L;

            Assert.True(freeKb > 0, "DriveInfo('/').AvailableFreeSpace should be positive on Linux.");
        }
    }
}
