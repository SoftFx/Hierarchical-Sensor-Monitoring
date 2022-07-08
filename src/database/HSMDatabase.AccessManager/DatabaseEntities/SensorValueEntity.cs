namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public record SensorValueEntity
    {
        public string SensorId { get; init; }

        public long ReceivingTime { get; init; }

        public object Value { get; init; }
    }
}
