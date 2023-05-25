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
        public Action<ActionType, Policy> Uploaded;


        internal abstract void Update(List<DataPolicyUpdate> updates);

        internal abstract void Attach(BaseSensorModel sensor);
    }


    public abstract class DataPolicyCollection<T> : DataPolicyCollection where T : BaseValue
    {
        private readonly CorrectDataTypePolicy<T> _typePolicy = new();


        protected abstract bool CalculateStorageResult(T value);

        internal abstract void Add(DataPolicy<T> policy);


        internal bool TryValidate(BaseValue value, out T valueT)
        {
            valueT = value as T;

            Result = _typePolicy.Validate(valueT);

            return Result.IsOk && CalculateStorageResult(valueT);
        }
    }


    public sealed class DataPolicyCollection<T, U> : DataPolicyCollection<T>
        where T : BaseValue
        where U : DataPolicy<T>, new()
    {
        private readonly ConcurrentDictionary<Guid, U> _storage = new();

        private BaseSensorModel _sensor;


        internal override IEnumerable<Guid> Ids => _storage.Keys;


        protected override bool CalculateStorageResult(T value)
        {
            foreach (var (_, policy) in _storage)
                Result += policy.Validate(value, _sensor);

            Result += PolicyResult.FromValue(value); //add user status

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

        internal override void Attach(BaseSensorModel sensor) => _sensor = sensor;

        public override IEnumerator<Policy> GetEnumerator() => _storage.Values.GetEnumerator();
    }
}
