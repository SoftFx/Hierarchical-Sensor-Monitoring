using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public sealed class JournalService : IJournalService
{
    private readonly IDatabaseCore _database;

    public event Action<JournalRecordModel> NewJournalEvent;


    public JournalService(IDatabaseCore database)
    {
        _database = database;
    }


    public void AddRecord(JournalRecordModel record)
    {
        _database.AddJournalValue(record.Key, record.ToJournalEntity());
        NewJournalEvent?.Invoke(record);
    }

    public void RemoveRecords(Guid id, Guid parentId) => _database.RemoveJournalValues(id, parentId);
    
    public async IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request)
    {
        var pages = _database.GetJournalValuesPage(request.Id, request.From, request.To, request.FromType, request.ToType, request.Count);

        await foreach (var page in pages)
        {
            var currPage = new List<JournalRecordModel>(1 << 4);

            currPage.AddRange(page.Select(x => new JournalRecordModel(x.Entity, x.Key)));

            yield return currPage;
        }
    }
}