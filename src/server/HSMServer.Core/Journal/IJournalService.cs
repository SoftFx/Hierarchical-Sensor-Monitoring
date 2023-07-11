using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    event Action<JournalRecordModel> NewJournalEvent;
    
    void AddJournal(JournalRecordModel record);

    void RemoveJournal(Guid id);

    IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(JournalHistoryRequestModel request);
}