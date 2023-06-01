namespace HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity
{
    public record SensorStateEntity
    {
        public long HistoryFrom { get; init; }

        public long HistoryTo { get; init; }
    }
}