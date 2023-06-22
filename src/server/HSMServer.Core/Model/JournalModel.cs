using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model;

public class JournalModel
{
    public Guid Id { get; set; }
    
    public long Time { get; set; }
    
    public string Name { get; set; }

    internal JournalEntity ToJournalEntity() => new()
    {
        Id = new Key(Id, Time),
        Name = Name
    };
}