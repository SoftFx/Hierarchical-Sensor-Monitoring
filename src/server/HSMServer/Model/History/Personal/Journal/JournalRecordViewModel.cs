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

    /// <summary>The wire id of the record (rollback-by-record addressing, #1588).</summary>
    public string Id { get; set; }

    public bool Rollbackable { get; set; }

    /// <summary>The record's operation tag (create/update/delete/rollback, #1638); null when not derivable.</summary>
    public string Operation { get; set; }


    public JournalRecordViewModel(JournalRecordModel model)
    {
        (Value, SearchValue) = BuildSearchAndViewValue(model);

        // The operation badge of the per-node tab (#1638): the same label
        // set the global Journal table shows. Derived from the record's own
        // data only — the tab streams records without undo-group access, so
        // policy rows (whose operation lives on the group entry) render no
        // badge here.
        Operation = Core.Journal.JournalOperations.Of(model);

        var badge = ManagementApi.Journal.JournalOperationLabels.Badge(Operation);

        if (badge.Length > 0)
        {
            Value = $"{badge} {Value}";
            SearchValue = $"{SearchValue} {ManagementApi.Journal.JournalOperationLabels.Label(Operation)}";
        }

        Type = model.Key.Type;
        Time = new DateTime(model.Key.Time);
        TimeAsString = Time.ToDefaultFormat();
        Initiator = model.Initiator;
        Path = model.Path;
        Id = ManagementApi.Journal.JournalRecordId.Of(model);
        Rollbackable = Core.Journal.JournalKinds.IsRollbackable(model.Kind) && model.ChangeSetId is not null;
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