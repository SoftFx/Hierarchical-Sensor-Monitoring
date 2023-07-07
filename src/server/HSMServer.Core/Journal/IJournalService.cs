using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    void AddJournal(JournalRecordModel record);

    void AddJournals(List<JournalRecordModel> records);
    void AddJournals(BaseSensorModel model, SensorUpdate update);
    
    void RemoveJournal(Guid id);

    IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(Guid id, DateTime from, DateTime to, RecordType type, int count);
}