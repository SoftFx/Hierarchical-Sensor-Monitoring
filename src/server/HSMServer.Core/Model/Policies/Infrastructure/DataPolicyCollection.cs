using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicyCollection : PolicyCollectionBase<Policy>
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


    public abstract class DataPolicyCollection<T> : DataPolicyCollection where T : BaseValue
    {
        private CorrectDataTypePolicy<T> _typePolicy;
        protected BaseSensorModel _sensor;

        protected abstract bool CalculateStorageResult(T value);

        internal abstract void Add(DataPolicy<T> policy);


        internal bool TryValidate(BaseValue value, out T valueT)
        {
            SensorResult = SensorResult.Ok;

            valueT = value as T;

            if (!CorrectDataTypePolicy<T>.Validate(valueT))
            {
                SensorResult = _typePolicy.SensorResult;
                PolicyResult = _typePolicy.PolicyResult;

                return false;
            }

            return CalculateStorageResult(valueT);
        }

        internal override void Attach(BaseSensorModel sensor)
        {
            _typePolicy = new CorrectDataTypePolicy<T>(sensor.Id);
            _sensor = sensor;
        }
    }


    public sealed class DataPolicyCollection<T, U> : DataPolicyCollection<T>
        where T : BaseValue
        where U : DataPolicy<T>, new()
    {
        private readonly ConcurrentDictionary<Guid, U> _storage = new();


        internal override IEnumerable<Guid> Ids => _storage.Keys;


        protected override bool CalculateStorageResult(T value)
        {
            if (!PolicyResult.IsOk)
                PolicyResult = new(_sensor.Id);

            foreach (var (_, policy) in _storage)
                if (!policy.Validate(value, _sensor))
                {
                    PolicyResult.AddAlert(policy);
                    SensorResult += policy.SensorResult;
                }

            return true;
        }


        internal override void Add(DataPolicy<T> policy) => _storage.TryAdd(policy.Id, (U)policy);

        internal override void Update(List<DataPolicyUpdate> updatesList)
        {
            if (updatesList == null)
                return;

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
                    _storage.TryRemove(id, out var oldPolicy);
                    Uploaded?.Invoke(ActionType.Delete, oldPolicy);
                }
            }

            foreach (var update in updatesList)
                if (update.Id == Guid.Empty)
                {
                    var policy = new U();

                    policy.Update(update);

                    Add(policy);
                    Uploaded?.Invoke(ActionType.Add, policy);
                }
        }


        public override IEnumerator<Policy> GetEnumerator() => _storage.Values.GetEnumerator();
    }
}
