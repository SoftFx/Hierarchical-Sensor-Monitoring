using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public abstract class PolicyCollectionBase : IEnumerable<Policy>
    {
        internal abstract IEnumerable<Guid> Ids { get; }


        public TTLPolicy TimeToLive { get; private set; }


        internal abstract void AddPolicy<T>(T policy) where T : Policy;

        internal abstract void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies);


        public abstract IEnumerator<Policy> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        internal virtual void BuildDefault(BaseNodeModel node, PolicyEntity entity = null) => TimeToLive = new TTLPolicy(node, entity);

        public virtual void UpdateTTL(PolicyUpdate update) => TimeToLive.FullUpdate(update); // TODO: should be internal after policies migration
    }
}