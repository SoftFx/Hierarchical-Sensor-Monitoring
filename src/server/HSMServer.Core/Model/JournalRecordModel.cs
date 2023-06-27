using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public Guid Id { get; set; }

    public long Time { get; set; }

    public string Value { get; set; }

    public JournalEntity ToJournalEntity() => new()
    {
        Value = Value
    };

    public JournalRecordModel(JournalEntity entity, Guid id)
    {
        Id = id;
        Value = entity.Value;
    }

    public JournalRecordModel(){}
}