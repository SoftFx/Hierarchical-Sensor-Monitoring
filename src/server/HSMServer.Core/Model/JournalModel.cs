using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public class JournalModel
{
    public Guid Id { get; set; }
    
    public long Time { get; set; }
    
    public string Name { get; set; }

    internal JournalEntity ToJournalEntiry() => new()
    {
        Key = new Key(Id, Time),
        Name = Name
    };
}