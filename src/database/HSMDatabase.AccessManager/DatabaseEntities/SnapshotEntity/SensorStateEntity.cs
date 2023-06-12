namespace HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity
{
    public record SensorStateEntity
    {
        public bool IsExpired { get; init; }

        public long HistoryFrom { get; init; }

        public long HistoryTo { get; init; }
    }
}