using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record TimeIntervalEntity
    {
        public long Interval { get; init; }

        [JsonPropertyName("CustomPeriod")]
        public long Ticks { get; init; }
    }
}