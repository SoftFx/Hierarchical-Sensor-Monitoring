using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    event Action<JournalRecordModel> NewJournalEvent;
    

    void AddRecord(JournalRecordModel record);

    void RemoveRecords(Guid id, Guid parentId = default);

    IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request);
}