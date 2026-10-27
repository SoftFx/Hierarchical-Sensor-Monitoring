using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Journal;

public interface IJournalService
{
    event Action<JournalRecordModel> NewRecordEvent;
    

    void AddRecord(JournalRecordModel record);

    void RemoveRecords(Guid id, Guid parentId = default);

    IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request);

    /// <summary>
    /// Every CHANGES-type record of the time range across ALL nodes (the
    /// global Journal surface's read, #1588) — filtering and paging belong
    /// to the caller.
    /// </summary>
    List<JournalRecordModel> GetRecords(DateTime from, DateTime to);
}