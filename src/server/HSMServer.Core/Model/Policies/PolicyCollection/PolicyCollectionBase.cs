using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model.NodeSettings;
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

        public IReadOnlyList<TTLPolicy> TTLPolicies
        {
            get { lock (_ttlLock) return _ttlPolicies; }
        }


        public event Action<JournalRecordModel> ChangesHandler;


        // Source used to resolve a TTL policy with IsTTLFromParent == true.
        // Default: the owning model's Settings.TTL (walks the parent chain — sensor behaviour).
        // ProductPolicyCollection overrides this to bound resolution to the node itself.
        protected virtual TimeIntervalSettingProperty TTLParentSource => _model.Settings.TTL;


        internal virtual void Attach(BaseNodeModel model) => _model = model;

        internal virtual void BuildDefault(BaseNodeModel node, PolicyEntity entity = null)
        {
            lock (_ttlLock)
            {
                if (entity != null)
                {
                    var policy = new TTLPolicy(node, entity);
                    if (policy.IsTTLFromParent)
                        policy.SetTTLParent(TTLParentSource);

                    _ttlPolicies = [policy];
                }
                else
                    _ttlPolicies = [];
            }
        }

        internal void BuildDefault(BaseNodeModel node, List<PolicyEntity> entities)
        {
            lock (_ttlLock)
            {
                _ttlPolicies = [];

                if (entities is null)
                    return;

                var built = new List<TTLPolicy>(entities.Count);
                foreach (var e in entities)
                {
                    var policy = new TTLPolicy(node, e);
                    if (policy.IsTTLFromParent)
                        policy.SetTTLParent(TTLParentSource);

                    built.Add(policy);
                }

                _ttlPolicies = built;
            }
        }


        internal void UpdateTTLs(List<PolicyUpdate> updates, InitiatorInfo initiator)
        {
            if (updates == null || (updates.Count == 0 && initiator == InitiatorInfo.AlertTemplate))
                return;

            var updatesDict = updates
                .Where(u => u.Id != Guid.Empty)
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.Last());
            var newPolicyUpdates = updates.Where(u => u.Id == Guid.Empty).ToList();
            var isTemplateInitiated = initiator == InitiatorInfo.AlertTemplate
                || updates.Any(u => u.Initiator == InitiatorInfo.AlertTemplate);

            var journalEntries = new List<(string oldValue, TTLPolicy policy, PolicyUpdate update, bool isParent)>();

            lock (_ttlLock)
            {
                var newList = new List<TTLPolicy>(_ttlPolicies.Count + newPolicyUpdates.Count);

                foreach (var policy in _ttlPolicies)
                {
                    if (updatesDict.TryGetValue(policy.Id, out var update))
                    {
                        var oldValue = policy.ToString();

                        if (policy.TemplateId != null && !update.Initiator.IsForceUpdate && update.Initiator != InitiatorInfo.AlertTemplate)
                        {
                            if (policy.IsDisabled != update.IsDisabled)
                            {
                                policy.SetDisabled(update.IsDisabled);
                                journalEntries.Add((oldValue, policy, update, update.IsParentRequest));
                            }
                            else
                            {
                                newList.Add(policy);
                                updatesDict.Remove(update.Id);
                                continue;
                            }
                        }
                        else
                        {
                            policy.FullUpdate(update);

                            if (!update.TTL.HasValue)
                                policy.SetTTLParent(TTLParentSource);

                            journalEntries.Add((oldValue, policy, update, update.IsParentRequest));
                        }
                        newList.Add(policy);
                        updatesDict.Remove(update.Id);
                    }
                    else if (policy.TemplateId != null || isTemplateInitiated)
                    {
                        // Preserve TTL policies not targeted by this update.
                        // Template-initiated updates only carry template TTL entries,
                        // so manual and other-template policies must be preserved.
                        newList.Add(policy);
                    }
                }

                foreach (var update in newPolicyUpdates.Concat(updatesDict.Values))
                {
                    var policy = new TTLPolicy();
                    policy.FullUpdate(update, _model as BaseSensorModel);

                    if (!update.TTL.HasValue)
                        policy.SetTTLParent(TTLParentSource);

                    journalEntries.Add((string.Empty, policy, update, false));
                    newList.Add(policy);
                }

                _ttlPolicies = newList;
            }

            foreach (var (oldValue, policy, update, isParent) in journalEntries)
                CallJournal(update.Id, oldValue, policy.ToString(), update.Initiator, isParent);
        }


        internal void AddTTLPolicy(PolicyUpdate update)
        {
            var policy = new TTLPolicy();
            policy.FullUpdate(update, _model as BaseSensorModel);

            if (!update.TTL.HasValue)
                policy.SetTTLParent(TTLParentSource);

            lock (_ttlLock)
                _ttlPolicies = [.._ttlPolicies, policy];

            CallJournal(update.Id, string.Empty, policy.ToString(), update.Initiator);
        }

        internal void AddTTLPolicy(TTLPolicy policy)
        {
            if (policy.IsTTLFromParent)
                policy.SetTTLParent(TTLParentSource);

            lock (_ttlLock)
                _ttlPolicies = [.._ttlPolicies, policy];
        }

        internal void RemoveTTLPolicy(Guid id)
        {
            lock (_ttlLock)
            {
                var newList = new List<TTLPolicy>(_ttlPolicies.Count);
                foreach (var p in _ttlPolicies)
                    if (p.Id != id)
                        newList.Add(p);

                if (newList.Count != _ttlPolicies.Count)
                    _ttlPolicies = newList;
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
