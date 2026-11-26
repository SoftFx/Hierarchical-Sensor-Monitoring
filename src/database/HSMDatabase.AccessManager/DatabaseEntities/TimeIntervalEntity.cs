using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record TimeIntervalEntity
    {
        public long Interval { get; init; }

        public long Ticks { get; init; }

        // The calendar preset an interval inherited from a folder resolves to (#1864); absent for every other interval,
        // so what an earlier build stored, and reads, is unchanged.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? FolderPreset { get; init; }


        [JsonConstructor]
        public TimeIntervalEntity(long interval, long ticks)
        {
            Interval = interval;
            Ticks = ticks;
        }
    }
}