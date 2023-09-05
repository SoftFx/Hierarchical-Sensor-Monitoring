using HSMServer.Core.TableOfChanges;
using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Core.Model.Requests;

public sealed record ClearHistoryRequest
{
    public required Guid Id { get; init; }


    public InitiatorInfo Initiator { get; init; } = InitiatorInfo.System;

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
    public ClearHistoryRequest(Guid id, InitiatorInfo initiator) : this(id)
    {
        Initiator = initiator;
    }
}