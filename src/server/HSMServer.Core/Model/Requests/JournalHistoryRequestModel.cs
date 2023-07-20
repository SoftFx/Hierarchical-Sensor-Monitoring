using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using System;

namespace HSMServer.Core.Model.Requests;

public sealed record JournalHistoryRequestModel
{
    public static RecordType AllTypes { get; } = (RecordType)(1 << Enum.GetValues<RecordType>().Length) - 1;


    public required Guid Id { get; init; }


    public DateTime From { get; init; } = DateTime.MinValue;

    public DateTime To { get; init; } = DateTime.MaxValue;

    public int Count { get; init; } = TreeValuesCache.MaxHistoryCount;


    public RecordType Types { get; init; } = AllTypes;
}