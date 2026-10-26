using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.ChangeSets
{
    // Domain model of the undo journal (#1570). The storage shape is
    // ChangeSetEntity (one JSON row per change set in the ChangeSets LevelDB
    // table); these are the Guid-ergonomic twins the administration services
    // and the rollback engine manipulate.

    public enum ChangeSetStatus : byte
    {
        // All recorded entries still reference live entities and nothing was
        // restored yet — the change set is fully rollbackable.
        Applied = 0,

        // Terminal: every entry was restored by a rollback change set.
        RolledBack = 1,

        // Some affected entities were deleted (their entries/images dropped
        // by the deletion cleanup); the surviving entries remain rollbackable.
        Partial = 2,
    }

    public enum ChangeSetEntryKind : byte
    {
        // A sensor's full data-policy list was replaced (Before/After images).
        SensorDataPolicies = 0,

        // A sensor's full TTL-policy list was replaced (Before/After images).
        SensorTtlPolicies = 1,

        // A product's own TTL-policy list was replaced (Before/After images).
        ProductTtlPolicies = 2,

        // Alert template create/update/delete. BeforeTemplate/AfterTemplate
        // carry the template row; Fanout carries per-sensor policy-list images
        // of every sensor the apply/remove touched.
        AlertTemplate = 3,

        // Alert schedule create/update/delete. BeforeSchedule/AfterSchedule
        // carry the schedule row; on delete, Fanout carries the per-node
        // images of the schedule-detach walk and Before/AfterTemplates the
        // template rows whose policies referenced the schedule.
        AlertSchedule = 4,
    }

    public enum ChangeSetOperation : byte
    {
        Create = 0,
        Update = 1,
        Delete = 2,
    }

    public enum ChangeSetNodeKind : byte
    {
        Sensor = 0,
        Product = 1,
    }


    // Per-node policy-list image of a fan-out write (template apply/remove,
    // schedule-delete detach). Both lists are snapshotted in full — the
    // compensating write is a full-list restore.
    public sealed record NodePolicyImage
    {
        public ChangeSetNodeKind NodeKind { get; init; }

        public Guid NodeId { get; init; }

        // Root product id — the authorization boundary of the node.
        public Guid RootProductId { get; init; }

        public string Path { get; init; }

        public List<PolicyEntity> BeforePolicies { get; init; }

        public List<PolicyEntity> AfterPolicies { get; init; }

        public List<PolicyEntity> BeforeTtlPolicies { get; init; }

        public List<PolicyEntity> AfterTtlPolicies { get; init; }
    }


    public sealed class ChangeSetEntry
    {
        // Append order inside the change set; assigned by the journal.
        public int Sequence { get; internal set; }

        public ChangeSetEntryKind Kind { get; init; }

        public ChangeSetOperation Operation { get; init; }

        public Guid EntityId { get; init; }

        public Guid? ParentId { get; init; }

        public string Path { get; init; }

        public List<PolicyEntity> Before { get; init; }

        public List<PolicyEntity> After { get; init; }

        public AlertTemplateEntity BeforeTemplate { get; init; }

        public AlertTemplateEntity AfterTemplate { get; init; }

        public AlertScheduleEntity BeforeSchedule { get; init; }

        public AlertScheduleEntity AfterSchedule { get; init; }

        public List<NodePolicyImage> Fanout { get; init; }

        public List<AlertTemplateEntity> BeforeTemplates { get; init; }

        public List<AlertTemplateEntity> AfterTemplates { get; init; }

        // Set by the rollback engine as it restores this entry; a retry of a
        // partially failed rollback skips already-restored entries instead of
        // tripping the conflict check on restored content.
        public Guid? RestoredBy { get; internal set; }
    }


    public sealed class ChangeSetModel
    {
        public Guid Id { get; init; }

        public string Author { get; init; }

        public InitiatorType AuthorType { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public ChangeSetStatus Status { get; internal set; }

        public List<ChangeSetEntry> Entries { get; init; } = [];
    }


    public static class ChangeSetMapper
    {
        public static ChangeSetEntity ToEntity(ChangeSetModel model) => new()
        {
            Id = model.Id.ToByteArray(),
            Author = model.Author,
            AuthorType = (byte)model.AuthorType,
            CreatedAtTicks = model.CreatedAtUtc.Ticks,
            Status = (byte)model.Status,
            Entries = model.Entries.Select(ToEntity).ToList(),
        };

        public static ChangeSetModel ToModel(ChangeSetEntity entity) => new()
        {
            Id = new Guid(entity.Id),
            Author = entity.Author,
            AuthorType = (InitiatorType)entity.AuthorType,
            CreatedAtUtc = new DateTime(entity.CreatedAtTicks, DateTimeKind.Utc),
            Status = (ChangeSetStatus)entity.Status,
            Entries = entity.Entries?.Select(ToModel).ToList() ?? [],
        };

        public static ChangeSetEntryEntity ToEntity(ChangeSetEntry entry) => new()
        {
            Sequence = entry.Sequence,
            Kind = (byte)entry.Kind,
            Operation = (byte)entry.Operation,
            EntityId = entry.EntityId.ToByteArray(),
            ParentId = entry.ParentId?.ToByteArray(),
            Path = entry.Path,
            Before = entry.Before,
            After = entry.After,
            BeforeTemplate = entry.BeforeTemplate,
            AfterTemplate = entry.AfterTemplate,
            BeforeSchedule = entry.BeforeSchedule,
            AfterSchedule = entry.AfterSchedule,
            Fanout = entry.Fanout?.Select(ToEntity).ToList(),
            BeforeTemplates = entry.BeforeTemplates,
            AfterTemplates = entry.AfterTemplates,
            RestoredBy = entry.RestoredBy?.ToByteArray(),
        };

        public static ChangeSetEntry ToModel(ChangeSetEntryEntity entity) => new()
        {
            Sequence = entity.Sequence,
            Kind = (ChangeSetEntryKind)entity.Kind,
            Operation = (ChangeSetOperation)entity.Operation,
            EntityId = new Guid(entity.EntityId),
            ParentId = entity.ParentId is { Length: 16 } parent ? new Guid(parent) : null,
            Path = entity.Path,
            Before = entity.Before,
            After = entity.After,
            BeforeTemplate = entity.BeforeTemplate,
            AfterTemplate = entity.AfterTemplate,
            BeforeSchedule = entity.BeforeSchedule,
            AfterSchedule = entity.AfterSchedule,
            Fanout = entity.Fanout?.Select(ToModel).ToList(),
            BeforeTemplates = entity.BeforeTemplates,
            AfterTemplates = entity.AfterTemplates,
            RestoredBy = entity.RestoredBy is { Length: 16 } restoredBy ? new Guid(restoredBy) : null,
        };

        private static NodePolicyImageEntity ToEntity(NodePolicyImage image) => new()
        {
            NodeKind = (byte)image.NodeKind,
            NodeId = image.NodeId.ToByteArray(),
            RootProductId = image.RootProductId.ToByteArray(),
            Path = image.Path,
            BeforePolicies = image.BeforePolicies,
            AfterPolicies = image.AfterPolicies,
            BeforeTtlPolicies = image.BeforeTtlPolicies,
            AfterTtlPolicies = image.AfterTtlPolicies,
        };

        private static NodePolicyImage ToModel(NodePolicyImageEntity entity) => new()
        {
            NodeKind = (ChangeSetNodeKind)entity.NodeKind,
            NodeId = new Guid(entity.NodeId),
            RootProductId = new Guid(entity.RootProductId),
            Path = entity.Path,
            BeforePolicies = entity.BeforePolicies,
            AfterPolicies = entity.AfterPolicies,
            BeforeTtlPolicies = entity.BeforeTtlPolicies,
            AfterTtlPolicies = entity.AfterTtlPolicies,
        };
    }
}
