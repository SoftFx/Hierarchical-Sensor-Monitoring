using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public abstract class PolicyCollectionBase : IEnumerable<Policy>
    {
        internal abstract IEnumerable<Guid> Ids { get; }


        internal Action<BaseSensorModel, bool> SensorExpired;


        internal abstract void AddPolicy<T>(T policy) where T : Policy;

        internal abstract void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies);


        public abstract IEnumerator<Policy> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}