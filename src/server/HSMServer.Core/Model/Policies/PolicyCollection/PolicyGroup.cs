using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyGroup
    {
        public ConcurrentDictionary<Guid, Policy> Policies { get; } = new();


        public Guid Id { get; } = Guid.NewGuid();

        public bool IsEmpty => Policies.IsEmpty;

        public string Template { get; private set; }


        public void AddPolicy(Policy policy)
        {
            if (IsEmpty)
                Template = policy.ToString();

            Policies.TryAdd(policy.Id, policy);
        }

        public void RemovePolicy(Guid id) => Policies.TryRemove(id, out _);
    }
}