using System;
using System.Collections.Generic;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public class JournalService : IJournalService
{
    private readonly IDatabaseCore _database;

    public event Action<JournalRecordModel> NewJournalEvent;

    public JournalService(IDatabaseCore database)
    {
        _database = database;
    }

    public void AddJournal(JournalRecordModel record)
    {
        if (!string.IsNullOrEmpty(record.Value))
        {
            _database.AddJournalValue(record.Key, record.ToJournalEntity());
            NewJournalEvent?.Invoke(record);
        }
    }

    public void RemoveJournal(Guid id) => _database.RemoveJournalValue(id);
    
    public async IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(JournalHistoryRequestModel request)
    {
        var pages = _database.GetJournalValuesPage(request.Id, request.From, request.To, request.FromType, request.ToType, request.Count);

        await foreach (var page in pages)
        {
            var currPage = new List<JournalRecordModel>(1 << 4);
            foreach (var item in page)
            {
                currPage.Add(new JournalRecordModel(JsonSerializer.Deserialize<JournalEntity>(item.Entity), item.Key));
            }
                
            yield return currPage;
        }
    }
}