namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record SensorEntity : BaseNodeEntity
    {
        public string ProductId { get; init; }

        public byte Type { get; init; }

        public byte State { get; init; }

        public long EndOfMuting { get; init; }
    }
}