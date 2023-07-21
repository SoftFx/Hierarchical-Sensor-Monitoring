using System;
using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    [Obsolete("Remove after policy migrations")]
    public sealed record OldTimeIntervalEntity
    {
        public long TimeInterval { get; init; }

        public long CustomPeriod { get; init; }


        public OldTimeIntervalEntity() { }

        public OldTimeIntervalEntity(long interval, long ticks)
        {
            TimeInterval = interval;
            CustomPeriod = ticks;
        }
    }


    public sealed record TimeIntervalEntity
    {
        public long Interval { get; init; }

        public long Ticks { get; init; }


        [JsonConstructor]
        public TimeIntervalEntity(long interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }
    }
}