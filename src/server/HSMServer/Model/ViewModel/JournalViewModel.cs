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
    
    public string Initiator { get; set; }
    
    public string Name { get; set; }

    
    public JournalViewModel(JournalRecordModel model)
    {
        Type = model.Key.Type;
        TimeAsString = new DateTime(model.Key.Time).ToDefaultFormat();
        Value = model.Value;
        Initiator = model.Initiator;
        Name = model.Path;
    }
}