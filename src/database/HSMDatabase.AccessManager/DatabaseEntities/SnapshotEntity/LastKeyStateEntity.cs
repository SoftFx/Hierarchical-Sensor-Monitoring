namespace HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;

public sealed record LastKeyStateEntity
{
    public string IP { get; set; }
    
    public long LastUseTicks { get; set; }
}