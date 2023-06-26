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
        Value = Value
    };

    public JournalModel(JournalEntity entity, Key key)
    {
        Id = key.Id;
        Time = key.Time;
        Value = entity.Value;
    }
}