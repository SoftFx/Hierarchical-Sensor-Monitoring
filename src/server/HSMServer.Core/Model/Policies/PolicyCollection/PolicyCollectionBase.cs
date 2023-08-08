using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections;
using System.Collections.Generic;
using HSMServer.Core.Journal;

namespace HSMServer.Core.Model.Policies
{
    public abstract class PolicyCollectionBase : IEnumerable<Policy>, IChangesEntity
    {
        internal abstract IEnumerable<Guid> Ids { get; }


        public TTLPolicy TimeToLive { get; private set; }


        internal Action<BaseSensorModel, bool, bool> SensorExpired;

        public event Action<JournalRecordModel> ChangesHandler;

        internal abstract void AddPolicy<T>(T policy) where T : Policy;

        internal abstract void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies);


        public abstract IEnumerator<Policy> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        internal virtual void BuildDefault(BaseNodeModel node, PolicyEntity entity = null) => TimeToLive = new TTLPolicy(node, entity);

        internal virtual void UpdateTTL(PolicyUpdate update) => TimeToLive.Update(update);


        protected void CallJournal(JournalRecordModel record) => ChangesHandler?.Invoke(record);
    }
}