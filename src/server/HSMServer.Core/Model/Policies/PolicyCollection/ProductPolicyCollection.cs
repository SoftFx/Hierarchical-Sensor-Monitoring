using HSMServer.Core.Cache;
using HSMServer.Core.Model.NodeSettings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class ProductPolicyCollection : PolicyCollectionBase
    {
        // _templateToGroup is the single source of truth; GetOrAdd ensures all callers
        // converge on one group per template, so the old _groups lookup is unnecessary.
        private readonly ConcurrentDictionary<string, PolicyGroup> _templateToGroup = new();

        private readonly ConcurrentDictionary<Guid, PolicyGroup> _policyToGroup = new();

        // AddPolicy and RemovePolicy are serialized under _gate so the empty-group
        // cleanup in RemovePolicy cannot race with a concurrent AddPolicy re-populating
        // the same group (which would orphan the newly-added policy). Reads
        // (SaveStateToExportGroup) stay lock-free — ConcurrentDictionary is safe for
        // concurrent reads.
        private readonly object _gate = new();

        internal IReadOnlyDictionary<string, PolicyGroup> TemplateToGroup => _templateToGroup;

        internal IReadOnlyDictionary<Guid, PolicyGroup> PolicyToGroup => _policyToGroup;

        // Bounded view of the owning product's Settings.TTL: same CurValue, no parent chain.
        // "From Parent" on a node-level TTL alert resolves against THIS node's own setting
        // and stops here — Never if the node itself has no explicit TTL.
        // Reads through to the source on every access, so later changes to Settings.TTL
        // are picked up without event subscriptions.
        private readonly BoundedTtlSource _boundedTtl = new();


        protected override TimeIntervalSettingProperty TTLParentSource => _boundedTtl;

        internal override void Attach(BaseNodeModel model)
        {
            base.Attach(model);
            _boundedTtl.SetSource(() => model.Settings.TTL.CurValue);
        }


        public PolicyExportGroup SaveStateToExportGroup(PolicyExportGroup exportGroup, string relativePath, Predicate<Guid> filter)
        {
            foreach (var (template, group) in _templateToGroup)
            {
                var info = group.Policies.Where(u => filter(u.Value.Sensor.Id))
                                         .Select(p => new PolicyExportInfo(p.Value, relativePath, exportGroup.CurrentProduct))
                                         .ToList();

                if (info.Count > 0)
                {
                    if (exportGroup.TryGetValue(template, out var policies))
                        policies.AddRange(info);
                    else
                        exportGroup.TryAdd(template, info);
                }
            }

            return exportGroup;
        }


        internal void ReceivePolicyUpdate(ActionType type, Policy policy)
        {
            var policyId = policy.Id;

            if (type is ActionType.Add)
                AddPolicy(policy);
            else if (type is ActionType.Delete)
                RemovePolicy(policyId);
            else if (type is ActionType.Update)
            {
                RemovePolicy(policyId);
                AddPolicy(policy);
            }
        }


        internal void AddPolicy(Policy policy)
        {
            lock (_gate)
            {
                // Pre-check under the lock so a duplicate policyId no-ops without
                // creating an orphan empty group via GetOrAdd.
                if (_policyToGroup.ContainsKey(policy.Id))
                    return;

                var group = _templateToGroup.GetOrAdd(policy.ToString(), template => new PolicyGroup(template));

                _policyToGroup[policy.Id] = group;
                group.AddPolicy(policy);
            }
        }

        private void RemovePolicy(Guid policyId)
        {
            lock (_gate)
            {
                if (_policyToGroup.TryRemove(policyId, out var group))
                {
                    group.RemovePolicy(policyId);

                    if (group.IsEmpty)
                        _templateToGroup.TryRemove(group.Template, out _);
                }
            }
        }


        // Self-bounded TTL source: behaves like a TimeIntervalSettingProperty whose CurValue
        // is whatever the owning product's Settings.TTL holds, but with no parent chain —
        // IsFromParent values fall through to EmptyValue (Never).
        private sealed class BoundedTtlSource : TimeIntervalSettingProperty
        {
            private Func<TimeIntervalModel> _source;

            internal void SetSource(Func<TimeIntervalModel> source) => _source = source;

            private TimeIntervalModel Current => _source?.Invoke();

            public override bool IsSet => Current is { } current && !current.IsFromParent;

            public override TimeIntervalModel Value =>
                Current is { } current && !current.IsFromParent ? current : EmptyValue;

            public override string GetJournalValue(string customNone = null)
            {
                var current = Current;
                if (current is null || current.IsNone)
                    return customNone ?? EmptyValue.ToString();
                return current.ToString();
            }
        }
    }
}
