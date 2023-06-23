using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public class JournalModel
{
    public Guid Id { get; set; }
    
    public long Time { get; set; }
    
    public string Value { get; set; }

    internal JournalEntity ToJournalEntity() => new()
    {
        Id = new Key(Id, Time),
        Value = Value
    };

    public JournalModel(JournalEntity entity)
    {
        Id = entity.Id.Id;
        Time = entity.Id.Time;
        Value = entity.Value;
    }
}