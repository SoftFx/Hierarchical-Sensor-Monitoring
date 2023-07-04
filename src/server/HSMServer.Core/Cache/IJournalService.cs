using System;
using System.Collections.Generic;
using HSMServer.Core.Model;

namespace HSMServer.Core.Cache;

public interface IJournalService
{
    void AddJournal(JournalRecordModel journalRecordModel);

    void AddJournals(List<JournalRecordModel> journalRecordModels);
    
    void RemoveJournal(Guid id);
}