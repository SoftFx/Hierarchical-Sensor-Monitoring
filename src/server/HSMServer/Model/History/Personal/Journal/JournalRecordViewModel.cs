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

    public string SearchValue { get; set; }

    public string Path { get; set; }


    public JournalRecordViewModel(JournalRecordModel model)
    {
        (Value, SearchValue) = BuildSearchAndViewValue(model);
        Type = model.Key.Type;
        TimeAsString = new DateTime(model.Key.Time).ToDefaultFormat();
        Initiator = model.Initiator;
        Path = model.Path;
    }


    private static (string ViewValue, string SearchValue) BuildSearchAndViewValue(JournalRecordModel model)
    {
        if (string.IsNullOrEmpty(model.PropertyName))
            return (model.Enviroment, $"{model.Enviroment} {model.Initiator}");

        var header = string.Empty;
        var value = string.Empty;

        if (string.IsNullOrEmpty(model.OldValue))
        {
            header = "Added new";
            value = model.NewValue;
        }
        else if (string.IsNullOrEmpty(model.NewValue))
        {   
            header = "Removed";
            value = model.OldValue;
        }

        if (header != string.Empty)
            return ($"""
            {header} {model.PropertyName}:
            <strong>{value}</strong>
            """, $"{model.PropertyName} {model.OldValue} {model.NewValue} {model.Initiator}");
        
        return ($"""
            {model.PropertyName} was modified
            Old value: {model.OldValue}
            <strong>New value: {model.NewValue}</strong>
            """, $"{model.PropertyName} {model.OldValue} {model.NewValue} {model.Initiator}");
    }
}