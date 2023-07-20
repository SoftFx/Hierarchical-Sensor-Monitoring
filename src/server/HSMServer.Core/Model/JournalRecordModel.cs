using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; }


    public string Enviroment { get; init; }

    public string Initiator { get; init; }


    public string PropertyName { get; set; }

    public string OldValue { get; set; }

    public string NewValue { get; set; }

    public string Path { get; set; }


    public JournalRecordModel(byte[] key, JournalRecordEntity entity)
    {
        Key = JournalKey.FromBytes(key);

        Initiator = entity.Initiator;
        Enviroment = entity.Enviroment;

        PropertyName = entity.PropertyName;
        NewValue = entity.NewValue;
        OldValue = entity.OldValue;
        Path = entity.Path;
    }

    public JournalRecordModel(Guid id, string initiator)
    {
        Key = new JournalKey(id, DateTime.UtcNow.Ticks);
        Initiator = initiator;
    }

    public JournalRecordModel(Guid id, string message, string path, string initiator) : this(id, initiator)
    {
        OldValue = message;
        Path = path;
    }


    public JournalRecordEntity ToJournalEntity() => new()
    {
        Enviroment = Enviroment,
        Initiator = Initiator,
        OldValue = OldValue,
        NewValue = NewValue,
        PropertyName = PropertyName,
        Path = Path,
    };
}