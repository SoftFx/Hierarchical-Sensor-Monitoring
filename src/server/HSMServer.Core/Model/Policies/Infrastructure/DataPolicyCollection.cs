using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicyCollection
    {
        internal abstract IEnumerable<Guid> Ids { get; }

        public abstract IEnumerable<Policy> Policies { get; }

        internal protected PolicyResult Result { get; protected set; } = PolicyResult.Ok;


        public Action<ActionType, Policy> Uploaded;


        internal void Reset() => Result = PolicyResult.Ok;
    }


    public sealed class DataPolicyCollection<T> : DataPolicyCollection where T : BaseValue
    {
        private readonly ConcurrentDictionary<Guid, DataPolicy<T>> _storage = new();
        private readonly CorrectDataTypePolicy<T> _typePolicy = new();

        internal override IEnumerable<Guid> Ids => _storage.Keys;

        public override IEnumerable<Policy> Policies => _storage.Values;


        internal bool TryValidate(BaseValue value, out T valueT)
        {
            valueT = value as T;

            Result = _typePolicy.Validate(valueT);

            if (!Result.IsOk)
                return false;

            foreach (var (_, policy) in _storage)
                Result += policy.Validate(valueT);

            Result += PolicyResult.FromValue(valueT); //add user status

            return true;
        }

        internal void Update(List<DataPolicyUpdate> updatesList)
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
                    _storage.TryRemove(id, out var oldPolicy);
                    Uploaded?.Invoke(ActionType.Delete, oldPolicy);
                }
            }

            foreach (var update in updatesList)
                if (update.Id == Guid.Empty)
                {
                    var policy = new IntegerDataPolicy().Update(update);

                    Add(policy as DataPolicy<T>);
                    Uploaded?.Invoke(ActionType.Add, policy);
                }
        }

        internal void Add(DataPolicy<T> policy) => _storage.TryAdd(policy.Id, policy);
    }
}
