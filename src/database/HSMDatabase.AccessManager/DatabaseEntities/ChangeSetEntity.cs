using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    // One durable row of the undo journal (#1570): a change set — the group of
    // configuration writes a client tied together under one changeSetId (or a
    // single write that carried no id). Serialized as JSON into the dedicated
    // ChangeSets LevelDB table; the shape is transport-agnostic and holds only
    // configuration images, never secrets.
    //
    // Which payload fields are meaningful is defined by Kind (see
    // ChangeSetEntryKind in HSMServer.Core.ChangeSets): policy-list entries use
    // Before/After; template entries use BeforeTemplate/AfterTemplate plus
    // Fanout images; schedule entries use BeforeSchedule/AfterSchedule plus the
    // detach fan-out (Fanout and BeforeTemplates/AfterTemplates).
    public sealed record ChangeSetEntity : IBaseEntity
    {
        public byte[] Id { get; set; }

        // Initiator display name and type (InitiatorInfo) of the FIRST write.
        public string Author { get; set; }

        public byte AuthorType { get; set; }

        // UTC creation ticks of the first write.
        public long CreatedAtTicks { get; set; }

        public byte Status { get; set; }

        public List<ChangeSetEntryEntity> Entries { get; set; }
    }


    public sealed record ChangeSetEntryEntity
    {
        public int Sequence { get; set; }

        public byte Kind { get; set; }

        public byte Operation { get; set; }

        // The entity the write targeted: sensor/product id for policy-list
        // entries, template id, schedule id.
        public byte[] EntityId { get; set; }

        // Authorization boundary for the rollback pre-flight: the ROOT product
        // id for sensor entries, the folder id for template entries, null for
        // schedules (global).
        public byte[] ParentId { get; set; }

        // Display path at recording time (sensor full path, template or
        // schedule name) — the entity may be renamed or gone by rollback time.
        public string Path { get; set; }

        // Policy-list images (sensor data, sensor TTL, product TTL entries).
        public List<PolicyEntity> Before { get; set; }

        public List<PolicyEntity> After { get; set; }

        // Template images (template entries).
        public AlertTemplateEntity BeforeTemplate { get; set; }

        public AlertTemplateEntity AfterTemplate { get; set; }

        // Schedule images (schedule entries).
        public AlertScheduleEntity BeforeSchedule { get; set; }

        public AlertScheduleEntity AfterSchedule { get; set; }

        // Per-node policy-list images of a fan-out write: template apply/remove
        // and the schedule-delete detach (sensors and products alike).
        public List<NodePolicyImageEntity> Fanout { get; set; }

        // Template rows touched by a schedule-delete detach (their policies'
        // ScheduleId is nulled); the schedule entry carries before/after rows.
        public List<AlertTemplateEntity> BeforeTemplates { get; set; }

        public List<AlertTemplateEntity> AfterTemplates { get; set; }

        // The rollback change set that already restored this entry (null while
        // not restored). A partially failed rollback marks the entries it
        // restored so a retry skips them instead of tripping the conflict
        // check on already-restored content.
        public byte[] RestoredBy { get; set; }
    }


    public sealed record NodePolicyImageEntity
    {
        public byte NodeKind { get; set; } // 0 = sensor, 1 = product

        public byte[] NodeId { get; set; }

        // Root product id — the authorization boundary of the node.
        public byte[] RootProductId { get; set; }

        public string Path { get; set; }

        public List<PolicyEntity> BeforePolicies { get; set; }

        public List<PolicyEntity> AfterPolicies { get; set; }

        public List<PolicyEntity> BeforeTtlPolicies { get; set; }

        public List<PolicyEntity> AfterTtlPolicies { get; set; }
    }
}
