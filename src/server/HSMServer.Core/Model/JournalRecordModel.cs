using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; set; }

    public string Value { get; set; }
    
    public string Initiator { get; set; }
    
    public string Path { get; set; }


    public JournalRecordModel(){}
    
    public JournalRecordModel(JournalEntity entity, byte[] key)
    {
        Value = entity.Value;
        Initiator = entity.Initiator;
        Path = entity.Name;
        Key = JournalKey.FromBytes(key);
    }

    public JournalRecordModel(Guid id, DateTime date, string message, string path, string initiator = "")
    {
        Value = message;
        Key = new JournalKey(id, date.Ticks);
        Initiator = initiator ?? TreeValuesCache.System;
        Path = path;
    }
    
    
    public JournalEntity ToJournalEntity() => new(Value, Path, Initiator);
}