namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    /// <summary>
    /// The delta math of the TCP connection failure rate sensor: fed the cumulative
    /// failed-connection-attempt counter of each address family on every tick, it returns the
    /// combined IPv4 + IPv6 increase to push into the rate sensor, or nothing when there is
    /// nothing to post.
    ///
    /// A line-for-line mirror of the native <c>TcpFailureRateAccumulator</c>
    /// (<c>src/native/collector/src/tcp_connection_stats.cpp</c>); root rule 10 asks for one
    /// algorithm on both sides. The two unit suites run the same cases with the same numbers.
    /// It has no OS dependency, so those cases run everywhere.
    /// </summary>
    internal sealed class TcpFailureRateAccumulator
    {
        /// <summary>
        /// One address family's reading for a single tick. An unreadable family (the OS read
        /// failed) contributes nothing and keeps its baseline, so it resumes without a false spike.
        /// </summary>
        internal readonly struct FamilyReading
        {
            internal static readonly FamilyReading Unreadable = default;

            internal FamilyReading(ulong attemptFails)
            {
                Readable = true;
                AttemptFails = attemptFails;
            }

            internal bool Readable { get; }

            internal ulong AttemptFails { get; }
        }

        // One baseline PER address family. A single baseline for the sum would let a family that
        // drops out and returns read as a counter reset: its whole cumulative count would come
        // back as one false spike, or real failures would be dropped.
        private struct FamilyBaseline
        {
            internal ulong Previous;
            internal bool HasBaseline;
        }

        private FamilyBaseline _v4;
        private FamilyBaseline _v6;

        /// <summary>
        /// The combined increase since the previous readable tick, or <c>null</c> when neither
        /// family is readable, on a family's first reading (it seeds the baseline), on a counter
        /// reset or wrap with no other progress, and on a zero delta: a quiet host posts nothing
        /// and the rate stays 0.
        /// </summary>
        internal double? Accumulate(FamilyReading v4, FamilyReading v6)
        {
            var anyReadable = false;
            ulong totalDelta = 0;

            AccumulateFamily(ref _v4, v4, ref anyReadable, ref totalDelta);
            AccumulateFamily(ref _v6, v6, ref anyReadable, ref totalDelta);

            if (!anyReadable || totalDelta == 0)
                return null;

            return totalDelta;
        }

        private static void AccumulateFamily(ref FamilyBaseline family, FamilyReading reading, ref bool anyReadable, ref ulong totalDelta)
        {
            if (!reading.Readable)
                return;

            anyReadable = true;

            var current = reading.AttemptFails;

            if (!family.HasBaseline)
            {
                family.Previous = current;
                family.HasBaseline = true;
                return;
            }

            // A counter that went backwards was reset or wrapped: add nothing, take the new baseline.
            if (current >= family.Previous)
                totalDelta += current - family.Previous;

            family.Previous = current;
        }
    }
}
