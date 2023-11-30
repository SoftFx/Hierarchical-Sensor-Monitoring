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

    public DateTime Time { get; set; }

    public string Value { get; set; }

    public string SearchValue { get; set; }

    public string Path { get; set; }


    public JournalRecordViewModel(JournalRecordModel model)
    {
        (Value, SearchValue) = BuildSearchAndViewValue(model);
        Type = model.Key.Type;
        Time = new DateTime(model.Key.Time);
        TimeAsString = Time.ToDefaultFormat();
        Initiator = model.Initiator;
        Path = model.Path;
    }


    private static (string ViewValue, string SearchValue) BuildSearchAndViewValue(JournalRecordModel model)
    {
        if (string.IsNullOrEmpty(model.PropertyName))
            return (model.Enviroment, $"{model.Enviroment} {model.Initiator}");

        var header = string.Empty;
        var value = string.Empty;

        if (string.IsNullOrEmpty(model.NewValue))
            return ($"""
                     {model.PropertyName} has been removed: 
                     <strong>{model.OldValue}</strong>
                     """, $"{model.PropertyName} {model.OldValue} {model.Initiator}");

        if (string.IsNullOrEmpty(model.OldValue) || model.Enviroment == "Added new value")
        {
            header = "Added new";
            value = model.NewValue;
        }

        var changeText = $"{model.PropertyName} {model.OldValue} {model.NewValue} {model.Initiator}";

        if (header != string.Empty)
            return ($"""
            {header} {model.PropertyName}:
            <strong>{value}</strong>
            """, changeText);

        return ($"""
            {model.PropertyName} was modified
            Old value: {model.OldValue}
            <strong>New value: {model.NewValue}</strong>
            """, changeText);
    }
}