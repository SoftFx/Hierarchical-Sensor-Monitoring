namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record SensorEntity : BaseNodeEntity
    {
        public string ProductId { get; init; }

        public string Unit { get; init; } //TODO remove unit


        public byte Type { get; init; }

        public byte State { get; init; }

        public long EndOfMuting { get; init; }
    }
}