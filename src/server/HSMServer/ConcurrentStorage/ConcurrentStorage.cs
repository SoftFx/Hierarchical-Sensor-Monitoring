using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.ConcurrentStorage
{
    public abstract class ConcurrentStorage<ModelType, EntityType, UpdateType> : ConcurrentDictionary<Guid, ModelType>
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateModel
    {
        protected abstract Action<EntityType> AddToDb { get; }

        protected abstract Action<EntityType> UpdateInDb { get; }

        protected abstract Action<ModelType> RemoveFromDb { get; }

        protected abstract Func<List<EntityType>> GetFromDb { get; }


        public new ModelType this[Guid id] => this.GetValueOrDefault(id);

        public ModelType this[Guid? id] => id.HasValue ? this[id.Value] : null;


        public event Action<ModelType> Added;
        public event Action<ModelType> Updated;
        public event Action<ModelType, InitiatorInfo> Removed;


        protected abstract ModelType FromEntity(EntityType entity);

        public bool TryGetValueById(Guid? id, out ModelType model)
        {
            model = null;

            return id is not null && TryGetValue(id.Value, out model);
        }

        public List<ModelType> GetValues() => Values.ToList();

        public virtual bool TryAdd(EntityType entity, out ModelType model)
        {
            model = FromEntity(entity);

            return TryAdd(model.Id, model);
        }

        public virtual Task<bool> TryAdd(ModelType model)
        {
            var result = TryAdd(model.Id, model);

            if (result)
            {
                AddToDb(model.ToEntity());
                Added?.Invoke(model);
            }

            return Task.FromResult(result);
        }

        public virtual async Task<bool> TryUpdate(UpdateType update)
        {
            var result = TryGetValue(update.Id, out var model);

            if (result)
            {
                model.Update(update);
                result &= await TryUpdate(model);
            }

            return result;
        }

        public virtual Task<bool> TryUpdate(ModelType value)
        {
            var result = TryGetValue(value.Id, out var model);

            if (result)
            {
                UpdateInDb(model.ToEntity());
                Updated?.Invoke(model);
            }

            return Task.FromResult(result);
        }

        public virtual Task<bool> TryRemove(RemoveModel remove)
        {
            var result = TryRemove(remove.Id, out var model);

            if (result)
            {
                RemoveFromDb(model);
                Removed?.Invoke(model, remove.Initiator);
            }

            return Task.FromResult(result);
        }

        public virtual Task Initialize()
        {
            foreach (var entity in GetFromDb())
                TryAdd(entity, out var _);

            return Task.CompletedTask;
        }
    }
}
