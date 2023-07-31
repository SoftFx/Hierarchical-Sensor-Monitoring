using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
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