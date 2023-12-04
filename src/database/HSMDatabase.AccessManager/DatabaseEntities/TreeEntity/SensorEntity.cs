namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record SensorEntity : BaseNodeEntity
    {
        public string ProductId { get; init; }


        public byte Type { get; init; }

        public byte State { get; init; }


        public bool AggregateValues { get; init; }

        public int? OriginalUnit { get; init; }

        public bool IsSingleton { get; init; }

        public long EndOfMuting { get; init; }

        public int Integration { get; init; }

        public int Options { get; init; }
    }
}