using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SensorPolicyCollection : PolicyCollectionBase, IChangesEntity
    {
        internal protected SensorResult SensorResult { get; protected set; } = SensorResult.Ok;

        internal protected PolicyResult PolicyResult { get; protected set; } = PolicyResult.Ok;


        internal Action<ActionType, Policy> Uploaded;
        internal Action<BaseSensorModel, bool> SensorExpired;

        public event Action<JournalRecordModel> ChangesHandler;


        internal abstract void Update(List<PolicyUpdate> updates, InitiatorInfo initiator);

        internal abstract void Attach(BaseSensorModel sensor);

        internal abstract void AddDefault(Dictionary<Guid, string> connectedChats);


        internal void Reset()
        {
            SensorResult = SensorResult.Ok;
            PolicyResult = PolicyResult.Ok;
        }

        protected void CallJournal(JournalRecordModel record) => ChangesHandler?.Invoke(record);
    }


    public abstract class SensorPolicyCollection<T> : SensorPolicyCollection where T : BaseValue
    {
        private protected BaseSensorModel _sensor;
        private CorrectTypePolicy<T> _typePolicy;


        private protected ChangeCollection AlertChangeInfo => _sensor.ChangeTable.Policies;


        protected abstract bool CalculateStorageResult(T value, bool updateSensor);


        internal override void Attach(BaseSensorModel sensor)
        {
            _typePolicy = new CorrectTypePolicy<T>(sensor);
            _sensor = sensor;

            PolicyResult = new(sensor.Id);

            base.BuildDefault(sensor);
        }

        internal override void BuildDefault(BaseNodeModel node, PolicyEntity entity = null)
        {
            base.BuildDefault(node, entity);

            _typePolicy.Destination = node.Policies.TimeToLive.Destination;
            _typePolicy.RebuildState();
        }

        public override void UpdateTTL(PolicyUpdate update)
        {
            var oldValue = TimeToLive.ToString();

            base.UpdateTTL(update);

            CallJournal(update.Id, update.Id == Guid.Empty ? string.Empty : oldValue, TimeToLive.ToString(), update.Initiator);
        }


        internal bool TryValidate(BaseValue value, out T valueT, bool updateSensor = true)
        {
            valueT = value as T;

            if (!CorrectTypePolicy<T>.Validate(valueT))
            {
                SensorResult = _typePolicy.SensorResult;
                PolicyResult = _typePolicy.PolicyResult;

                return false;
            }

            return CalculateStorageResult(valueT, updateSensor);
        }

        internal bool SensorTimeout(BaseValue value)
        {
            if (value is null || value.Status.IsOfftime())
                return false;

            RemoveAlert(TimeToLive);

            var timeout = false;

            if (TimeToLive is not null && !TimeToLive.IsDisabled)
            {
                timeout = TimeToLive.HasTimeout(value.LastUpdateTime);

                if (timeout)
                {
                    PolicyResult.AddSingleAlert(TimeToLive);
                    SensorResult += TimeToLive.SensorResult;
                }
            }

            SensorExpired?.Invoke(_sensor, timeout);

            return timeout;
        }


        protected void CallJournal(Guid alertId, string oldValue, string newValue, InitiatorInfo initiator, bool isParentCall = false)
        {
            if (oldValue != newValue)
            {
                var propertyName = isParentCall ? "Alert change" : "Alert change by parent";

                CallJournal(new JournalRecordModel(_sensor.Id, initiator)
                {
                    Enviroment = "Alert collection",
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Path = _sensor.FullPath,
                });

                if (alertId != Guid.Empty)
                    AlertChangeInfo[alertId.ToString()].SetUpdate(initiator);
            }
        }

        private void RemoveAlert(Policy policy)
        {
            PolicyResult.RemoveAlert(policy);
            SensorResult -= policy.SensorResult;
        }
    }


    public sealed class SensorPolicyCollection<ValueType, PolicyType> : SensorPolicyCollection<ValueType>
        where ValueType : BaseValue
        where PolicyType : Policy<ValueType>, new()
    {
        private readonly ConcurrentDictionary<Guid, PolicyType> _storage = new();


        internal override IEnumerable<Guid> Ids => _storage.Keys;


        protected override bool CalculateStorageResult(ValueType value, bool updateStatus = true)
        {
            SensorResult = SensorResult.Ok;
            PolicyResult = new(_sensor.Id);

            foreach (var policy in _storage.Values)
                if (!policy.IsDisabled && !policy.Validate(value))
                {
                    PolicyResult.AddAlert(policy);

                    if (updateStatus)
                        SensorResult += policy.SensorResult;
                }

            return true;
        }


        internal override void AddPolicy<T>(T policy)
        {
            if (policy is PolicyType typedPolicy)
                _storage.TryAdd(policy.Id, typedPolicy);
        }

        internal override void Update(List<PolicyUpdate> updatesList, InitiatorInfo initiator)
        {
            var updates = updatesList.Where(u => u.Id != Guid.Empty).ToDictionary(u => u.Id);

            foreach (var (id, policy) in _storage)
                if (AlertChangeInfo[id.ToString()].CanChange(initiator))
                {
                    if (updates.TryGetValue(id, out var update))
                    {
                        var oldPolicy = policy.ToString();

                        policy.Update(update);

                        CallJournal(id, oldPolicy, policy.ToString(), initiator);
                        Uploaded?.Invoke(ActionType.Update, policy);
                    }
                    else if (_storage.TryRemove(id, out var oldPolicy))
                    {
                        CallJournal(id, oldPolicy.ToString(), string.Empty, initiator);
                        Uploaded?.Invoke(ActionType.Delete, oldPolicy);
                    }
                }

            foreach (var update in updatesList)
                if (update.Id == Guid.Empty)
                {
                    var policy = new PolicyType();

                    policy.Update(update, _sensor);

                    AddPolicy(policy);

                    CallJournal(policy.Id, string.Empty, policy.ToString(), initiator);
                    Uploaded?.Invoke(ActionType.Add, policy);
                }

            if (_sensor?.LastValue is ValueType valueT)
            {
                CalculateStorageResult(valueT);
                SensorTimeout(valueT);
            }
        }

        public override IEnumerator<Policy> GetEnumerator() => _storage.Values.GetEnumerator();

        internal override void ApplyPolicies(List<string> policyIds, Dictionary<string, PolicyEntity> allPolicies)
        {
            foreach (var id in policyIds ?? Enumerable.Empty<string>())
                if (allPolicies.TryGetValue(id, out var entity))
                {
                    var policy = new PolicyType();

                    policy.Apply(entity, _sensor);

                    _storage.TryAdd(policy.Id, policy);
                }
        }

        internal override void AddDefault(Dictionary<Guid, string> connectedChats)
        {
            var policy = new PolicyType();

            var statusUpdate = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Status = SensorStatus.Ok,
                Template = $"$prevStatus->$status [$product]$path = $comment",
                Destination = new(true, connectedChats),
                Conditions = new(1)
                {
                    new PolicyConditionUpdate(
                        PolicyOperation.IsChanged,
                        PolicyProperty.Status,
                        new TargetValue(TargetType.LastValue, _sensor.Id.ToString())),
                },
            };

            policy.Update(statusUpdate, _sensor);

            AddPolicy(policy);
            Uploaded?.Invoke(ActionType.Add, policy);
        }
    }
}