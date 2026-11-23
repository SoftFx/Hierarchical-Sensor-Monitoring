using System;

namespace HSMServer.Core.DataLayer
{
    /// <summary>
    /// The daily UTC window a background maintenance job works in (the sealing job, #1804; the history compaction, #1801).
    /// A start equal to the end is the whole day; a window may wrap past midnight ("22:00" to "06:00"). The end is exclusive.
    /// </summary>
    public readonly record struct DailyUtcWindow(TimeSpan StartUtc, TimeSpan EndUtc)
    {
        public bool IsAllDay => StartUtc == EndUtc;

        public bool Contains(DateTime utcNow)
        {
            var time = utcNow.TimeOfDay;

            if (IsAllDay)
                return true;

            return StartUtc < EndUtc ? time >= StartUtc && time < EndUtc : time >= StartUtc || time < EndUtc;
        }

        /// <summary>"HH:mm-HH:mm", as the start lines of the jobs print it.</summary>
        public override string ToString() => $"{StartUtc:hh\\:mm}-{EndUtc:hh\\:mm}";
    }
}
