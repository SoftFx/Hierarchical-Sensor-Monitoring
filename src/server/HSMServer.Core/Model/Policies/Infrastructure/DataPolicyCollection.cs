﻿using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class DataPolicyCollection : IEnumerable<Policy>
    {
        internal abstract IEnumerable<Guid> Ids { get; }

        internal protected PolicyResult Result { get; protected set; } = PolicyResult.Ok;


        public Action<ActionType, Policy> Uploaded;


        internal abstract void Update(List<DataPolicyUpdate> updates);

        internal void Reset() => Result = PolicyResult.Ok;


        public abstract IEnumerator<Policy> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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


        internal override IEnumerable<Guid> Ids => _storage.Keys;


        protected override bool CalculateStorageResult(T value)
        {
            foreach (var (_, policy) in _storage)
                Result += policy.Validate(value);

            Result += PolicyResult.FromValue(value); //add user status

            return true;
        }

        internal override void Add(DataPolicy<T> policy) => _storage.TryAdd(policy.Id, (U)policy);

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
