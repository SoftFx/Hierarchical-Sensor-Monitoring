using HSMDatabase.AccessManager;
using HSMServer.Core.TreeStateSnapshot.States;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class StateCollection<StateType, EntityType> : ConcurrentDictionary<Guid, StateType>, ISnapshotCollection<StateType>
        where StateType : class, ILastState<EntityType>, new()
    {
        public new StateType this[Guid id] => GetOrAdd(id, new StateType());


        public StateCollection() : base() { }

        public StateCollection(IEntitySnapshotCollection<EntityType> values)
        {
            foreach (var (key, entity) in values.Read())
            {
                var state = new StateType();

                state.FromEntity(entity);
                TryAdd(key, state);
            }
        }


        public Dictionary<Guid, EntityType> GetStates()
        {
            return this.Where(u => !u.Value.IsDefault).ToDictionary(k => k.Key, v => v.Value.ToEntity());
        }
    }
}