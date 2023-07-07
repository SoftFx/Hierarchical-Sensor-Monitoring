using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.ViewModel;

public class JournalViewModel
{
    public RecordType Type { get; set; }
    
    public string TimeAsString { get; set; }
    
    public string Value { get; set; }
    
    public JournalViewModel(JournalRecordModel model)
    {
        Type = model.Key.Type;
        TimeAsString = new DateTime(model.Key.Time).ToDefaultFormat();
        Value = model.Value;
    }

    public string GenerateStatements()
    {
        return $"[{Type}] - {TimeAsString} - {Value}";
    }
}