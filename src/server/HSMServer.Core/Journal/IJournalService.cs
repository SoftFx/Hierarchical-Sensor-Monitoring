using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    event Action<JournalRecordModel> NewJournalEvent;
    

    void AddRecord(JournalRecordModel record);

    void RemoveRecord(Guid id);

    IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request);
}