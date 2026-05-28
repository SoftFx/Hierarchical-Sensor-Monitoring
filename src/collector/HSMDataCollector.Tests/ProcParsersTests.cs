using HSMDataCollector.DefaultSensors.Unix.SystemInfo;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Unit tests for the Linux /proc parsers (#1065). They run on any OS because the parsers are pure
    /// functions fed sample text — no actual /proc access.
    /// </summary>
    public sealed class ProcParsersTests
    {
        // --- /proc/stat CPU parsing ---

        [Fact]
        public void ProcStat_parses_aggregate_cpu_line()
        {
            // cpu  user nice system idle iowait irq softirq steal guest guest_nice
            const string sample = "cpu  100 0 50 1000 20 0 5 0 0 0\ncpu0 50 0 25 500 10 0 2 0 0 0\n";

            var times = ProcStat.ParseCpuTimes(sample);

            Assert.NotNull(times);
            // idle = idle field only (1000); iowait (20) is deliberately counted as busy.
            Assert.Equal(1000.0, times.Value.Idle);
            // total = 100+0+50+1000+20+0+5+0+0+0 = 1175
            Assert.Equal(1175.0, times.Value.Total);
        }

        [Fact]
        public void CpuUsage_counts_iowait_as_busy()
        {
            // Matches the previous top-based sensor (top's "id" excludes iowait).
            var usage = new ProcStatCpuUsage("cpu 0 0 0 1000 0 0 0 0 0 0\n");

            // total +100 all in iowait, idle unchanged -> 100% busy (iowait is busy).
            var busy = usage.NextBusyPercent("cpu 0 0 0 1000 100 0 0 0 0 0\n");

            Assert.Equal(100.0, busy.Value, 3);
        }

        [Fact]
        public void CpuUsage_clamps_lower_bound_to_zero()
        {
            // baseline: idle=1000, total=1100 (user=100).
            var usage = new ProcStatCpuUsage("cpu 100 0 0 1000 0 0 0 0 0 0\n");

            // next: idle=1120 (+120), user=0 -> total=1120 (+20). idleDelta(120) > totalDelta(20),
            // so raw busy = (1 - 120/20)*100 is negative -> must clamp to 0.
            var busy = usage.NextBusyPercent("cpu 0 0 0 1120 0 0 0 0 0 0\n");

            Assert.Equal(0.0, busy.Value, 3);
        }

        [Fact]
        public void CpuUsage_returns_null_on_counter_reset_negative_delta()
        {
            var usage = new ProcStatCpuUsage("cpu 500 0 0 1000 0 0 0 0 0 0\n"); // total 1500

            // Current totals lower than previous (reset) -> totalDelta <= 0 -> null.
            Assert.Null(usage.NextBusyPercent("cpu 10 0 0 100 0 0 0 0 0 0\n"));
        }

        [Fact]
        public void ProcStat_ignores_per_core_lines_and_requires_aggregate_first()
        {
            // First line is a per-core line, not the aggregate — must not be parsed as the aggregate.
            const string sample = "cpu0 50 0 25 500 10 0 2 0 0 0\ncpu  100 0 50 1000 20 0 5 0 0 0\n";

            Assert.Null(ProcStat.ParseCpuTimes(sample));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage line\n")]
        [InlineData("cpu  100 abc 50 1000\n")]
        public void ProcStat_returns_null_for_unparseable_input(string content)
        {
            Assert.Null(ProcStat.ParseCpuTimes(content));
        }

        [Fact]
        public void CpuUsage_first_sample_after_baseline_computes_busy_percent()
        {
            // Baseline: total=1000, idle=900.
            var usage = new ProcStatCpuUsage("cpu 100 0 0 900 0 0 0 0 0 0\n");

            // Next: total=1100 (+100), idle=950 (+50). idleDelta/totalDelta = 50/100 = 0.5 -> busy 50%.
            var busy = usage.NextBusyPercent("cpu 150 0 0 950 0 0 0 0 0 0\n");

            Assert.NotNull(busy);
            Assert.Equal(50.0, busy.Value, 3);
        }

        [Fact]
        public void CpuUsage_full_load_reads_hundred_percent()
        {
            var usage = new ProcStatCpuUsage("cpu 100 0 0 900 0 0 0 0 0 0\n");

            // total +100, idle +0 -> 100% busy.
            var busy = usage.NextBusyPercent("cpu 200 0 0 900 0 0 0 0 0 0\n");

            Assert.Equal(100.0, busy.Value, 3);
        }

        [Fact]
        public void CpuUsage_idle_reads_zero_percent()
        {
            var usage = new ProcStatCpuUsage("cpu 100 0 0 900 0 0 0 0 0 0\n");

            // total +100, idle +100 -> 0% busy.
            var busy = usage.NextBusyPercent("cpu 100 0 0 1000 0 0 0 0 0 0\n");

            Assert.Equal(0.0, busy.Value, 3);
        }

        [Fact]
        public void CpuUsage_returns_null_when_no_time_elapsed()
        {
            var usage = new ProcStatCpuUsage("cpu 100 0 0 900 0 0 0 0 0 0\n");

            // Identical sample -> totalDelta == 0 -> null (skipped bar).
            Assert.Null(usage.NextBusyPercent("cpu 100 0 0 900 0 0 0 0 0 0\n"));
        }

        [Fact]
        public void CpuUsage_returns_null_when_current_sample_unparseable()
        {
            var usage = new ProcStatCpuUsage("cpu 100 0 0 900 0 0 0 0 0 0\n");

            Assert.Null(usage.NextBusyPercent("garbage"));
        }

        // --- /proc/meminfo parsing ---

        [Fact]
        public void ProcMeminfo_parses_mem_available_kb()
        {
            const string sample =
                "MemTotal:       16384000 kB\n" +
                "MemFree:         1000000 kB\n" +
                "MemAvailable:    8192000 kB\n" +
                "Buffers:          200000 kB\n";

            Assert.Equal(8192000L, ProcMeminfo.ParseAvailableKb(sample));
        }

        [Fact]
        public void ProcMeminfo_handles_crlf_and_extra_spacing()
        {
            const string sample = "MemTotal:  16384000 kB\r\nMemAvailable:     500 kB\r\n";

            Assert.Equal(500L, ProcMeminfo.ParseAvailableKb(sample));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("MemTotal: 16384000 kB\n")] // no MemAvailable line
        [InlineData("MemAvailable:    notanumber kB\n")]
        public void ProcMeminfo_returns_null_when_absent_or_unparseable(string content)
        {
            Assert.Null(ProcMeminfo.ParseAvailableKb(content));
        }
    }
}
