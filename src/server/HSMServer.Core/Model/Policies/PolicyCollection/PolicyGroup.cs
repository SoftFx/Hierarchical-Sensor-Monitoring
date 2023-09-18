using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyGroup
    {
        private readonly ConcurrentDictionary<Guid, Policy> _policies = new();


        public Guid Id { get; } = Guid.NewGuid();

        public bool IsEmpty => _policies.IsEmpty;

        public string Template { get; private set; }

        public Policy Policy { get; private set; }


        public void AddPolicy(Policy policy)
        {
            if (IsEmpty)
            {
                Template = policy.ToString();
                Policy = policy;
            }

            _policies.TryAdd(policy.Id, policy);
        }

        public void RemovePolicy(Guid id) => _policies.TryRemove(id, out _);
    }
}