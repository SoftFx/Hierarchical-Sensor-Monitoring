using HSMServer.Core.Cache;
using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Core.Model.Requests;

public sealed record ClearHistoryRequest
{
    public required Guid Id { get; init; }


    public string Initiator { get; init; } = TreeValuesCache.System;

    public DateTime To { get; init; } = DateTime.MaxValue;


    [SetsRequiredMembers]
    public ClearHistoryRequest(Guid id)
    {
        Id = id;
    }

    [SetsRequiredMembers]
    public ClearHistoryRequest(Guid id, DateTime to) : this(id)
    {
        To = to;
    }

    [SetsRequiredMembers]
    public ClearHistoryRequest(Guid id, string initiator) : this(id)
    {
        Initiator = initiator;
    }
}