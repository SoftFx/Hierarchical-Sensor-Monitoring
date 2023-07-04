using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;

namespace HSMServer.Core.Cache;

public class JournalService : IJournalService
{
    private readonly IDatabaseCore _database;

    public JournalService(IDatabaseCore database)
    {
        _database = database;
    }

    public void AddJournal(JournalRecordModel journalRecordModel)
    {
        _database.AddJournalValue(journalRecordModel.GetKey(), journalRecordModel.ToJournalEntity());
    }
    
    public void AddJournals(List<JournalRecordModel> journalRecordModels)
    {
        foreach (var journal in journalRecordModels)
            AddJournal(journal);
            
        journalRecordModels.Clear();
    }
    
    public void RemoveJournal(Guid id) => _database.RemoveJournalValue(id);
}