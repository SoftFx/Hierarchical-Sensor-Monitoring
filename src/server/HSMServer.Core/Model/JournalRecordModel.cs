using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public Guid Id { get; set; }

    public long Time { get; set; }

    public string Value { get; set; }
    
    public JournalType Type { get; set; }
    

    public JournalRecordModel(){}
    
    public JournalRecordModel(JournalEntity entity, Guid id)
    {
        Id = id;
        Value = entity.Value;
    }

    public JournalRecordModel(Guid id, DateTime date, string message, JournalType type)
    {
        Id = id;
        Time = date.Ticks;
        Value = message;
        Type = type;
    }
    
    
    public JournalEntity ToJournalEntity() => new()
    {
        Value = Value
    };

    public Key GetKey() => new Key(Id, Time, Type);
}