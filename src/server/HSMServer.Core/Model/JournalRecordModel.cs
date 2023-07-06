using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; set; }
    
    public Guid Id { get; set; }

    public long Time { get; set; }

    public string Value { get; set; }

    public RecordType Type { get; set; }
    

    public JournalRecordModel(){}
    
    public JournalRecordModel(JournalEntity entity, Guid id)
    {
        Id = id;
        Value = entity.Value;
    }
    
    public JournalRecordModel(JournalEntity entity, byte[] key)
    {
        Value = entity.Value;
        Key = JournalKey.FromBytes(key);
        Id = Key.Id;
        Time = Key.Time;
        Type = Key.Type;
    }

    public JournalRecordModel(Guid id, DateTime date, string message, RecordType type = RecordType.Actions)
    {
        Id = id;
        Time = date.Ticks;
        Value = message;
        Type = type;
        Key = new JournalKey(Id, Time, Type);
    }
    
    
    public JournalEntity ToJournalEntity() => new(Value);

    public JournalKey GetKey() => new (Id, Time, Type);
}