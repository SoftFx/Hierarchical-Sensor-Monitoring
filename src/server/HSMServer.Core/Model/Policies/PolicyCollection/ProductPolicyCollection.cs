using HSMServer.Core.Cache;
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

        public List<PolicyGroup> GroupedPolicies => _groups.Values.ToList();


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
    }
}