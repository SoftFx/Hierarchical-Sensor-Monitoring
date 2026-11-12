using HSMServer.Core.Journal;

namespace HSMServer.Model.ManagementApi.Journal;

/// <summary>
/// The table-facing rendering of a journal record's operation (#1638): the
/// badge text and Bootstrap color of each operation tag, shared by the
/// global Journal table (_JournalTable) and the per-node tab's Record cell
/// (JournalRecordViewModel), so the two surfaces never drift apart.
/// </summary>
public static class JournalOperationLabels
{
    /// <summary>The badge text of the operation; null renders no badge.</summary>
    public static string Label(string operation) => operation switch
    {
        JournalOperations.Create => "Create",
        JournalOperations.Update => "Update",
        JournalOperations.Delete => "Delete",
        JournalOperations.Rollback => "Rollback",
        _ => null,
    };

    /// <summary>The Bootstrap badge class of the operation.</summary>
    public static string CssClass(string operation) => operation switch
    {
        JournalOperations.Create => "bg-success",
        JournalOperations.Update => "bg-secondary",
        JournalOperations.Delete => "bg-danger",
        JournalOperations.Rollback => "bg-warning text-dark",
        _ => "bg-secondary",
    };

    /// <summary>
    /// The ready badge markup of the operation (the per-node tab renders
    /// its cells as strings, not Razor); empty when the tag is unknown.
    /// </summary>
    public static string Badge(string operation) =>
        Label(operation) is { } label
            ? $"<span class=\"badge {CssClass(operation)}\">{label}</span>"
            : string.Empty;

    /// <summary>The rolled-back marker of a record whose undo group went terminal.</summary>
    public static string RolledBackBadge() =>
        "<span class=\"badge bg-light text-dark\" title=\"This write was undone by a later rollback\">rolled back</span>";
}
