using HSMDatabase.AccessManager.DatabaseEntities;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.ChangeSets
{
    /// <summary>
    /// Replay helpers of the undo journal (#1570): rebuild the full-list
    /// update payloads a rollback sends from the recorded entity images, and
    /// compare live state against an after-image by CONTENT (the #1570
    /// conflict rule — never timestamps).
    /// </summary>
    public static class ChangeSetSnapshotMapper
    {
        /// <summary>
        /// A data-policy image back into a full-list update item. The policy
        /// model is reconstructed from the entity row (the recorded policy may
        /// no longer exist live), then copied into the update shape the merge
        /// machinery uses.
        /// </summary>
        public static PolicyUpdate ToDataUpdate(PolicyEntity entity, SensorType sensorType,
            InitiatorInfo initiator, bool preserveOwnership = false)
        {
            var policy = Policy.BuildPolicy((byte)sensorType);
            policy.Apply(entity);

            return new PolicyUpdate(policy, initiator)
            {
                PreserveChangeOwnership = preserveOwnership,
            };
        }

        /// <summary>
        /// A TTL-policy image back into a full-list update item. The interval
        /// rides EXPLICITLY (a null TTL is an explicit reset-to-parent — the
        /// #1409/#1451 full-list rule), and the entity carries it verbatim.
        /// </summary>
        public static PolicyUpdate ToTtlUpdate(PolicyEntity entity, InitiatorInfo initiator,
            bool preserveOwnership = false)
        {
            var policy = new TTLPolicy(node: null, entity: entity);

            return new PolicyUpdate(policy, initiator)
            {
                TTL = entity.TTL,
                PreserveChangeOwnership = preserveOwnership,
            };
        }
    }


    /// <summary>
    /// Content comparison of policy-list images against live policy rows
    /// (#1570): order-insensitive by policy id, field-wise per policy, chat
    /// sets compared as SETS (dictionary order is not stable across a
    /// serialize/deserialize round trip).
    /// </summary>
    public static class PolicyImageComparer
    {
        // null (absent list) and empty are the same image.
        public static bool ListsEqual(List<PolicyEntity> image, List<PolicyEntity> live)
        {
            image ??= [];
            live ??= [];

            if (image.Count != live.Count)
                return false;

            var liveById = live.ToDictionary(p => new Guid(p.Id));

            return image.All(row =>
                liveById.TryGetValue(new Guid(row.Id), out var liveRow) && PoliciesEqual(row, liveRow));
        }

        public static bool PoliciesEqual(PolicyEntity left, PolicyEntity right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null || right is null)
                return false;

            return GuidOf(left.Id) == GuidOf(right.Id) &&
                   left.SensorStatus == right.SensorStatus &&
                   left.IsDisabled == right.IsDisabled &&
                   StringEquals(left.Template, right.Template) &&
                   StringEquals(left.Icon, right.Icon) &&
                   left.ConfirmationPeriod == right.ConfirmationPeriod &&
                   GuidOf(left.TemplateId) == GuidOf(right.TemplateId) &&
                   GuidOf(left.ScheduleId) == GuidOf(right.ScheduleId) &&
                   GuidOf(left.TemplateAlertId) == GuidOf(right.TemplateAlertId) &&
                   left.TTL == right.TTL &&
                   ConditionsEqual(left.Conditions, right.Conditions) &&
                   DestinationsEqual(left.Destination, right.Destination) &&
                   SchedulesEqual(left.Schedule, right.Schedule);
        }

        // A short human-readable divergence of two list images, one line per
        // added/removed/changed policy — the per-entity diff of the rollback
        // conflict report.
        public static List<string> DescribeListDiff(List<PolicyEntity> image, List<PolicyEntity> live)
        {
            image ??= [];
            live ??= [];

            var diff = new List<string>();
            var imageById = image.ToDictionary(p => new Guid(p.Id));
            var liveById = live.ToDictionary(p => new Guid(p.Id));

            foreach (var (id, row) in imageById)
                if (!liveById.TryGetValue(id, out var liveRow))
                    diff.Add($"policy {id}: expected in the after-image, absent now (removed)");
                else if (!PoliciesEqual(row, liveRow))
                    diff.Add($"policy {id}: content changed after the journaled write");

            foreach (var id in liveById.Keys.Where(id => !imageById.ContainsKey(id)))
                diff.Add($"policy {id}: added after the journaled write");

            return diff;
        }

        private static bool ConditionsEqual(List<PolicyConditionEntity> left, List<PolicyConditionEntity> right)
        {
            left ??= [];
            right ??= [];

            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                var l = left[i];
                var r = right[i];

                if (l.Operation != r.Operation || l.Property != r.Property || l.Combination != r.Combination)
                    return false;

                if (!StringEquals(l.Target?.Value, r.Target?.Value) || l.Target?.Type != r.Target?.Type)
                    return false;
            }

            return true;
        }

        private static bool DestinationsEqual(PolicyDestinationEntity left, PolicyDestinationEntity right) =>
            (left is null && right is null) ||
            (left is not null && right is not null &&
             StringEquals(left.Kind, right.Kind) &&
             left.IsNotInitialized == right.IsNotInitialized &&
             left.IsEmpty == right.IsEmpty &&
             left.UseDefaultChats == right.UseDefaultChats &&
             left.AllChats == right.AllChats &&
             ChatSetsEqual(left.Chats, right.Chats));

        private static bool ChatSetsEqual(Dictionary<string, string> left, Dictionary<string, string> right)
        {
            left ??= [];
            right ??= [];

            return left.Count == right.Count &&
                   left.All(pair => right.TryGetValue(pair.Key, out var value) && StringEquals(value, pair.Value));
        }

        private static bool SchedulesEqual(PolicyScheduleEntity left, PolicyScheduleEntity right) =>
            (left is null && right is null) ||
            (left is not null && right is not null &&
             left.TimeTicks == right.TimeTicks &&
             left.InstantSend == right.InstantSend &&
             left.RepeateMode == right.RepeateMode);

        private static bool StringEquals(string left, string right) =>
            string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

        private static Guid GuidOf(byte[] id) =>
            id is { Length: 16 } ? new Guid(id) : Guid.Empty;
    }
}
