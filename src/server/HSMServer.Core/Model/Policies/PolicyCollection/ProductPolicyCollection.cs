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
        private readonly ConcurrentDictionary<string, Guid> _templateToGroup = new();

        private readonly ConcurrentDictionary<Guid, PolicyGroup> _groups = new();
        private readonly ConcurrentDictionary<Guid, Guid> _policyToGroup = new();

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
            foreach (var (template, groupId) in _templateToGroup)
                if (_groups.TryGetValue(groupId, out var group))
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
            var template = policy.ToString();
            var policyId = policy.Id;

            if (!_templateToGroup.TryGetValue(template, out var groudId))
            {
                var newGroup = new PolicyGroup();

                groudId = newGroup.Id;

                _templateToGroup.TryAdd(template, groudId);
                _groups.TryAdd(groudId, newGroup);
            }

            if ((!_policyToGroup.ContainsKey(policyId) || _policyToGroup.TryRemove(policyId, out _)) && _policyToGroup.TryAdd(policyId, groudId))
                _groups[groudId].AddPolicy(policy);
        }

        private void RemovePolicy(Guid policyId)
        {
            if (_policyToGroup.TryRemove(policyId, out var groupId) && _groups.TryGetValue(groupId, out var group))
            {
                group.RemovePolicy(policyId);

                if (group.IsEmpty)
                {
                    _templateToGroup.TryRemove(group.Template, out _);
                    _groups.Remove(groupId, out _);
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
