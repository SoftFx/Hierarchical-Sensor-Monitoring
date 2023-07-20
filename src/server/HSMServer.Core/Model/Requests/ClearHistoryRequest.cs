using HSMServer.Core.Cache;
using System;

namespace HSMServer.Core.Model.Requests;

public sealed record ClearHistoryRequest
{
    public Guid Id { get; init; }


    public string Initiator { get; init; } = TreeValuesCache.System;

    public DateTime To { get; init; } = DateTime.MaxValue;


    public ClearHistoryRequest(Guid id) 
    {
        Id = id;
    }

    public ClearHistoryRequest(Guid id, DateTime to) : this(id)
    {
        To = to;
    }

    public ClearHistoryRequest(Guid id, string initiator) : this(id)
    {
        Initiator = initiator;
    }


    internal JournalRecordModel ToRecord(string path) => new JournalRecordModel(Id, Initiator)
    {
        Enviroment = "Clear sensor history",
        Path = path,
    };
}