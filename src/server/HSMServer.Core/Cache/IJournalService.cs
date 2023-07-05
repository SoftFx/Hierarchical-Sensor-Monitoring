using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;

namespace HSMServer.Core.Cache;

public interface IJournalService
{
    void AddJournal(JournalRecordModel journalRecordModel);

    void AddJournals(List<JournalRecordModel> journalRecordModels);
    
    void RemoveJournal(Guid id);

    IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(Guid id, DateTime from, DateTime to, RecordType recordType, int count);
}