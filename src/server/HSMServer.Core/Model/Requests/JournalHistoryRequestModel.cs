using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;

namespace HSMServer.Core.Model.Requests;

public record JournalHistoryRequestModel
{ 
    public Guid Id { get; init; }

    public DateTime From { get; init; } = DateTime.MinValue;

    public DateTime To { get; init; } = DateTime.MaxValue;

    public RecordType FromType { get; init; } = RecordType.Actions;

    public RecordType ToType { get; init; } = RecordType.Changes;

    public int Count { get; init; } = TreeValuesCache.MaxHistoryCount;
}