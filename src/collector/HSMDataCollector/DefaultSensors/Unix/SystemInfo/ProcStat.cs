using System;
using System.Globalization;


namespace HSMDataCollector.DefaultSensors.Unix.SystemInfo
{
    /// <summary>
    /// Aggregate CPU times parsed from the first ("cpu ") line of <c>/proc/stat</c>.
    /// Idle is the <c>idle</c> field only (iowait is intentionally counted as busy time, matching the
    /// previous <c>top -bn1 ... 100 - id</c> sensor, where <c>top</c>'s <c>id</c> excludes iowait).
    /// Total is the sum of all reported fields.
    /// </summary>
    internal readonly struct CpuTimes
    {
        internal CpuTimes(double idle, double total)
        {
            Idle = idle;
            Total = total;
        }

        internal double Idle { get; }

        internal double Total { get; }
    }


    /// <summary>
    /// Pure parser for the aggregate CPU line of <c>/proc/stat</c>. Kept free of file I/O so it can be
    /// unit-tested by feeding sample text on any OS.
    ///
    /// The "cpu " line format is:
    ///   cpu  user nice system idle iowait irq softirq steal guest guest_nice
    /// (counts in USER_HZ jiffies). Busy fraction over an interval is
    ///   1 - (idleDelta / totalDelta), where idle is the "idle" field only (iowait counts as busy).
    /// The guest fields are already included in user/nice by Linux and are excluded from total.
    /// </summary>
    internal static class ProcStat
    {
        // Index of the "idle" field within the split tokens: parts[0]="cpu", parts[1]=user,
        // parts[2]=nice, parts[3]=system, parts[4]=idle, parts[5]=iowait, ...
        private const int IdleFieldIndex = 4;
        private const int GuestFieldIndex = 9;
        private const int GuestNiceFieldIndex = 10;

        internal static CpuTimes? ParseCpuTimes(string procStatContent)
        {
            if (string.IsNullOrEmpty(procStatContent))
                return null;

            var newline = procStatContent.IndexOf('\n');
            var firstLine = newline >= 0 ? procStatContent.Substring(0, newline) : procStatContent;

            // Expect the aggregate line, which starts with "cpu" followed by whitespace
            // (the per-core lines are "cpu0", "cpu1", ... and must not match).
            var parts = firstLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= IdleFieldIndex || !string.Equals(parts[0], "cpu", StringComparison.Ordinal))
                return null;

            double total = 0.0;
            double idle = 0.0;

            for (var i = 1; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    return null;

                if (i != GuestFieldIndex && i != GuestNiceFieldIndex)
                    total += value;

                if (i == IdleFieldIndex) // idle field only; iowait is deliberately left in "busy"
                    idle += value;
            }

            return new CpuTimes(idle, total);
        }
    }


    /// <summary>
    /// Computes total CPU busy percentage from successive <c>/proc/stat</c> samples. Holds the previous
    /// sample and returns the busy fraction over the interval since the last call. Not thread-safe;
    /// each sensor owns its own instance and samples it from its single collect loop.
    /// </summary>
    internal sealed class ProcStatCpuUsage
    {
        private CpuTimes? _previous;

        internal ProcStatCpuUsage(string initialProcStatContent)
        {
            _previous = ProcStat.ParseCpuTimes(initialProcStatContent);
        }

        /// <summary>
        /// Returns busy CPU percent (0..100) over the interval since the previous sample, or null when
        /// there is no usable baseline or no time elapsed (e.g. identical samples / unparseable input).
        /// </summary>
        internal double? NextBusyPercent(string procStatContent)
        {
            var current = ProcStat.ParseCpuTimes(procStatContent);
            if (current == null)
                return null;

            var previous = _previous;
            _previous = current;

            if (previous == null)
                return null;

            var totalDelta = current.Value.Total - previous.Value.Total;
            var idleDelta = current.Value.Idle - previous.Value.Idle;

            if (totalDelta <= 0.0)
                return null;

            var busy = (1.0 - (idleDelta / totalDelta)) * 100.0;

            if (busy < 0.0)
                return 0.0;
            if (busy > 100.0)
                return 100.0;

            return busy;
        }
    }
}
