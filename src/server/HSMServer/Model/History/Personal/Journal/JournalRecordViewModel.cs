using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Controllers.DataTables;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.History.Personal.Journal;

public sealed class JournalRecordViewModel
{
    public string this[ColumnName name] => name switch
    {
        ColumnName.Date => TimeAsString,
        ColumnName.Path => Path,
        ColumnName.Type => Type.ToString(),
        ColumnName.Record => Value,
        ColumnName.Initiator => Initiator,
        _ => throw new NotImplementedException(),
    };


    public RecordType Type { get; set; }


    public string Initiator { get; set; }

    public string TimeAsString { get; set; }

    public string Value { get; set; }

    public string Path { get; set; }


    public JournalRecordViewModel(JournalRecordModel model)
    {
        Type = model.Key.Type;
        TimeAsString = new DateTime(model.Key.Time).ToDefaultFormat();
        Value = $"{model.PropertyName}: {model.OldValue} -> {model.NewValue}";
        Initiator = model.Initiator;
        Path = model.Path;
    }
}