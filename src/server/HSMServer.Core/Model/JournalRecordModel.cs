using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; set; }

    public string Value { get; set; }
    
    public string Initiator { get; set; }
    
    public string Path { get; set; }

    
    public JournalRecordModel(JournalEntity entity, byte[] key)
    {
        Value = entity.Value;
        Initiator = entity.Initiator;
        Path = entity.Path;
        Key = JournalKey.FromBytes(key);
    }

    public JournalRecordModel(Guid id, string message, string path, string initiator)
    {
        Value = message;
        Key = new JournalKey(id, DateTime.UtcNow.Ticks);
        Initiator = initiator;
        Path = path;
    }
    
    
    public JournalEntity ToJournalEntity() => new(Value, Path, Initiator);
}