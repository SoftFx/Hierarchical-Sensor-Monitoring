using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class PolicyCollectionBase : IChangesEntity
    {
        private protected BaseNodeModel _model;

        private protected ChangeCollection AlertChangeTable => _model.ChangeTable.Policies;

        private readonly object _ttlLock = new();
        private List<TTLPolicy> _ttlPolicies = [];

        public List<TTLPolicy> TTLPolicies
        {
            get { lock (_ttlLock) return _ttlPolicies.ToList(); }
        }


        public event Action<JournalRecordModel> ChangesHandler;


        internal virtual void Attach(BaseNodeModel model) => _model = model;

        internal virtual void BuildDefault(BaseNodeModel node, PolicyEntity entity = null)
        {
            lock (_ttlLock)
            {
                if (entity != null)
                    _ttlPolicies = [new TTLPolicy(node, entity)];
                else
                    _ttlPolicies = [];
            }
        }

        internal void BuildDefault(BaseNodeModel node, List<PolicyEntity> entities)
        {
            lock (_ttlLock)
                _ttlPolicies = entities?.Select(e => new TTLPolicy(node, e)).ToList() ?? [];
        }


        internal void UpdateTTLs(List<PolicyUpdate> updates)
        {
            if (updates == null)
                return;

            var updatesDict = updates
                .Where(u => u.Id != Guid.Empty)
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.Last());
            var newPolicies = updates.Where(u => u.Id == Guid.Empty).ToList();

            List<TTLPolicy> currentPolicies;
            lock (_ttlLock)
                currentPolicies = _ttlPolicies.ToList();

            // Update existing policies
            foreach (var policy in currentPolicies)
            {
                if (updatesDict.TryGetValue(policy.Id, out var update))
                {
                    var oldValue = policy.ToString();
                    policy.FullUpdate(update);
                    CallJournal(update.Id, oldValue, policy.ToString(), update.Initiator, update.IsParentRequest);
                    updatesDict.Remove(policy.Id);
                }
                else
                {
                    RemoveTTLPolicy(policy.Id);
                }
            }

            // Add new policies
            foreach (var update in newPolicies.Concat(updatesDict.Values))
                AddTTLPolicy(update);
        }


        internal void AddTTLPolicy(PolicyUpdate update)
        {
            var policy = new TTLPolicy();
            policy.FullUpdate(update, _model as BaseSensorModel);

            lock (_ttlLock)
                _ttlPolicies.Add(policy);

            CallJournal(update.Id, string.Empty, policy.ToString(), update.Initiator);
        }

        internal void AddTTLPolicy(TTLPolicy policy)
        {
            lock (_ttlLock)
                _ttlPolicies.Add(policy);
        }

        internal void RemoveTTLPolicy(Guid id)
        {
            lock (_ttlLock)
            {
                var policy = _ttlPolicies.FirstOrDefault(p => p.Id == id);
                if (policy != null)
                    _ttlPolicies.Remove(policy);
            }
        }


        protected void CallJournal(Guid alertId, string oldValue, string newValue, InitiatorInfo initiator, bool isParentCall = false)
        {
            if (oldValue != newValue)
            {
                var propertyName = isParentCall ? "Alert (change by parent)" : "Alert";

                ChangesHandler?.Invoke(new JournalRecordModel(_model.Id, initiator)
                {
                    Enviroment = "Alert collection",
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Path = _model.FullPath,
                });

                if (alertId != Guid.Empty)
                    AlertChangeTable[alertId.ToString()].SetUpdate(initiator);
            }
        }
    }
}
