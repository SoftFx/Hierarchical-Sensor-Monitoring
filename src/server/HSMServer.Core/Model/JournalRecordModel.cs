using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
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

    public JournalRecordModel(Guid id, DateTime date, string message, RecordType type = RecordType.Actions)
    {
        Id = id;
        Time = date.Ticks;
        Value = message;
        Type = type;
    }
    
    
    public JournalEntity ToJournalEntity() => new(Value);

    public JournalKey GetKey() => new JournalKey(Id, Time, Type);
}