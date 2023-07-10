using System;
using System.Collections.Generic;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    void AddJournal(JournalRecordModel record);

    void AddJournals(List<JournalRecordModel> records);
    void AddJournals(BaseSensorModel model, SensorUpdate update);
    
    void RemoveJournal(Guid id);

    IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(JournalHistoryRequestModel request);
}