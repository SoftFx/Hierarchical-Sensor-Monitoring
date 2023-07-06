using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; set; }

    public string Value { get; set; }

    public JournalRecordModel(){}
    
    
    public JournalRecordModel(JournalEntity entity, byte[] key)
    {
        Value = entity.Value;
        Key = JournalKey.FromBytes(key);
    }

    public JournalRecordModel(Guid id, DateTime date, string message, RecordType type = RecordType.Actions)
    {
        Value = message;
        Key = new JournalKey(id, date.Ticks, type);
    }
    
    
    public JournalEntity ToJournalEntity() => new(Value);
}