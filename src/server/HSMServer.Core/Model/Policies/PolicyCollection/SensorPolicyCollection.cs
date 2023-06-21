using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class SensorPolicyCollection : PolicyCollectionBase
    {
        internal protected SensorResult SensorResult { get; protected set; } = SensorResult.Ok;

        internal protected PolicyResult PolicyResult { get; protected set; } = PolicyResult.Ok;


        internal Action<ActionType, Policy> Uploaded;


        internal abstract void Update(List<DataPolicyUpdate> updates);

        internal abstract void Attach(BaseSensorModel sensor);


        internal void Reset()
        {
            SensorResult = SensorResult.Ok;
            PolicyResult = PolicyResult.Ok;
        }
    }


    public abstract class SensorPolicyCollection<T> : SensorPolicyCollection where T : BaseValue
    {
        private CorrectTypePolicy<T> _typePolicy;
        private TTLPolicy _ttlPolicy;

        private protected BaseSensorModel _sensor;


        protected abstract bool CalculateStorageResult(T value);


        internal bool TryValidate(BaseValue value, out T valueT)
        {
            SensorResult = SensorResult.Ok;

            valueT = value as T;

            if (!CorrectTypePolicy<T>.Validate(valueT))
            {
                SensorResult = _typePolicy.SensorResult;
                PolicyResult = _typePolicy.PolicyResult;

                return false;
            }

            return CalculateStorageResult(valueT);
        }

        internal override void Attach(BaseSensorModel sensor)
        {
            _ttlPolicy = new TTLPolicy(sensor.Id, sensor.Settings.TTL);
            _typePolicy = new CorrectTypePolicy<T>(sensor.Id);

            _sensor = sensor;
        }


        internal bool SensorTimeout(DateTime? time)
        {
            var timeout = _ttlPolicy?.HasTimeout(time) ?? false;

            if (timeout)
                PolicyResult = _ttlPolicy.PolicyResult;

            return timeout;
        }
    }


    public sealed class SensorPolicyCollection<ValueType, PolicyType> : SensorPolicyCollection<ValueType>
        where ValueType : BaseValue
        where PolicyType : Policy<ValueType>, new()
    {
        private readonly ConcurrentDictionary<Guid, PolicyType> _storage = new();


        internal override IEnumerable<Guid> Ids => _storage.Keys;


        protected override bool CalculateStorageResult(ValueType value)
        {
            PolicyResult = new(_sensor.Id);

            foreach (var (_, policy) in _storage)
                if (!policy.Validate(value, _sensor))
                {
                    PolicyResult.AddAlert(policy);
                    SensorResult += policy.SensorResult;
                }

            return true;
        }


        internal override void AddPolicy<T>(T policy)
        {
            if (policy is PolicyType typedPolicy)
                _storage.TryAdd(policy.Id, typedPolicy);
        }

        internal override void Update(List<DataPolicyUpdate> updatesList)
        {
            var updates = updatesList.Where(u => u.Id != Guid.Empty).ToDictionary(u => u.Id);

            foreach (var (id, policy) in _storage)
            {
                if (updates.TryGetValue(id, out var update))
                {
                    policy.Update(update);
                    Uploaded?.Invoke(ActionType.Update, policy);
                }
                else
                {
                    if (_storage.TryRemove(id, out var oldPolicy))
                    {
                        CalculateStorageResult((ValueType)_sensor.LastValue);
                        Uploaded?.Invoke(ActionType.Delete, oldPolicy);
                    }
                }
            }

            foreach (var update in updatesList)
                if (update.Id == Guid.Empty)
                {
                    var policy = new PolicyType();

                    policy.Update(update);

                    AddPolicy(policy);
                    Uploaded?.Invoke(ActionType.Add, policy);
                }
        }

        public override IEnumerator<Policy> GetEnumerator() => _storage.Values.GetEnumerator();
    }
}