using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model.Requests;

public class JournalHistoryRequestModel
{
    public Guid Id { get; set; }
    
    public DateTime From { get; set; } = DateTime.MinValue;

    public DateTime To { get; set; } = DateTime.MaxValue;

    public int Count { get; set; } = 100;

    public RecordType Type { get; set; } = RecordType.Changes;
    
    public JournalHistoryRequestModel() { }

    public JournalHistoryRequestModel(Guid id, DateTime from, DateTime to, RecordType type, int count)
    {
        Id = id;
        Count = count;
        Type = type;
        From = from;
        To = to;
    }
}