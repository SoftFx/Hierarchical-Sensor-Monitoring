namespace HSMDatabase.AccessManager.DatabaseEntities;

public record JournalValueEntity
{
    public Key JournalKey { get; init; }
    
    public string Value { get; init; }
}