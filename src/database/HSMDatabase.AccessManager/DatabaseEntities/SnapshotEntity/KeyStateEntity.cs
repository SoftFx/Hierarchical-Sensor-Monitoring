namespace HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;

public record KeyStateEntity
{
    public string IP { get; set; }
    
    public long LastUseTime { get; set; }
}