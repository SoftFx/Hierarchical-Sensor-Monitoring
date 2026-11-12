using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Journal;

/// <summary>
/// The operation tag of unified-journal records (#1638): what kind of write
/// a record is — create, update, delete or rollback — derived from data the
/// stored rows already carry. The stored formats stay untouched: the
/// "{Kind}:{Operation}" property format and the "Rollback" enviroment marker
/// ARE the storage contract; the derived tag rides on the DTO as an
/// additive field.
/// </summary>
public static class JournalOperations
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Rollback = "rollback";

    /// <summary>
    /// The enviroment marker the recorder stamps rollback rows with: their
    /// property carries the INVERSE operation (the compensating write), so
    /// this marker is what names them a rollback — the redo handle.
    /// </summary>
    public const string RollbackEnviroment = "Rollback";

    /// <summary>
    /// Derive the record's operation. Priority: the rollback marker (the
    /// property of a rollback row names the COMPENSATING write, not the
    /// rollback itself), the operation part of the "{Kind}:{Operation}"
    /// property (template, schedule and rollback rows), the record's undo
    /// group entry (policy rows — the group resolver is optional and null
    /// on surfaces that stream records without group access, e.g. the
    /// per-node journal tab), then the legacy enviroment wording (tree CRUD
    /// and node settings rows). Null when nothing says — legacy rows.
    /// </summary>
    public static string Of(JournalRecordModel record,
        Func<string, ChangeSetModel> groupResolver = null)
    {
        if (record is null)
            return null;

        if (string.Equals(record.Enviroment, RollbackEnviroment, StringComparison.Ordinal))
            return Rollback;

        if (ParseFromProperty(record.PropertyName) is { } parsed)
            return parsed;

        if (groupResolver is not null &&
            Guid.TryParse(record.ChangeSetId, out var groupId) &&
            JournalKinds.IsRollbackable(record.Kind))
        {
            // One group can hold SEVERAL entries of one entity (its data
            // and TTL lists journaled as separate writes) — the entry of
            // the record's OWN kind decides, not the first hit.
            var entryKind = EntryKindOf(record.Kind);
            var entry = groupResolver(groupId.ToString())?.Entries
                .Find(e => e.EntityId == record.Key.Id && (entryKind is null || e.Kind == entryKind));

            if (entry is not null)
                return Of(entry.Operation);
        }

        return FromEnviroment(record.Enviroment);
    }

    // The record's kind tag mapped onto the undo-group entry kind; null for
    // unknown/legacy kinds (the entity-id match alone applies there).
    private static ChangeSetEntryKind? EntryKindOf(string kind) => kind switch
    {
        JournalKinds.SensorDataPolicies => ChangeSetEntryKind.SensorDataPolicies,
        JournalKinds.SensorTtlPolicies => ChangeSetEntryKind.SensorTtlPolicies,
        JournalKinds.ProductTtlPolicies => ChangeSetEntryKind.ProductTtlPolicies,
        JournalKinds.AlertTemplate => ChangeSetEntryKind.AlertTemplate,
        JournalKinds.AlertSchedule => ChangeSetEntryKind.AlertSchedule,
        _ => null,
    };

    public static string Of(ChangeSetOperation operation) => operation switch
    {
        ChangeSetOperation.Create => Create,
        ChangeSetOperation.Update => Update,
        ChangeSetOperation.Delete => Delete,
        _ => null,
    };

    // "AlertTemplate:Create" — the recorder's property format. Only the
    // exact operation names match: a property merely CONTAINING them must
    // not masquerade as the format.
    private static string ParseFromProperty(string property)
    {
        if (string.IsNullOrEmpty(property))
            return null;

        var separator = property.LastIndexOf(':');

        if (separator < 0 || separator == property.Length - 1)
            return null;

        return property[(separator + 1)..] switch
        {
            "Create" => Create,
            "Update" => Update,
            "Delete" => Delete,
            _ => null,
        };
    }

    // The pre-unification wording of tree CRUD and node settings rows
    // ("Create product", "Remove node", "Move product", "General info
    // update"); a wording that names no operation answers null. A move is
    // an update of the node's position, not a fourth operation.
    private static string FromEnviroment(string enviroment)
    {
        if (string.IsNullOrEmpty(enviroment))
            return null;

        if (enviroment.StartsWith("Create ", StringComparison.Ordinal))
            return Create;

        if (enviroment.StartsWith("Remove ", StringComparison.Ordinal))
            return Delete;

        if (enviroment.StartsWith("Move ", StringComparison.Ordinal))
            return Update;

        return enviroment.EndsWith(" update", StringComparison.Ordinal) ? Update : null;
    }
}
