using System;
using System.Collections.Generic;
using System.Text.Json;
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
    
    public async IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(Guid id, DateTime from, DateTime to, RecordType recordType, int count)
    {
        var pages = _database.GetJournalValuesPage(id, from, to, recordType, count);

        await foreach (var page in pages)
        {
            var currPage = new List<JournalRecordModel>(1 << 4);
            foreach (var item in page)
            {
                currPage.Add(new JournalRecordModel(JsonSerializer.Deserialize<JournalEntity>(item), id));
            }
                
            yield return currPage;
        }
    }
}