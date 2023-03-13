using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace HSMServer.ConcurrentStorage
{
    public abstract class ConcurrentStorage<ModelType, EntityType, UpdateType> : ConcurrentDictionary<Guid, ModelType>
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateModel
    {
        private readonly ConcurrentDictionary<string, Guid> _modelNames = new();


        protected abstract Action<EntityType> AddToDb { get; }

        protected abstract Action<EntityType> UpdateInDb { get; }

        protected abstract Action<ModelType> RemoveFromDb { get; }


        public event Action<ModelType> Added;
        public event Action<ModelType> Updated;
        public event Action<ModelType> Removed;


        internal ModelType this[string name] =>
            !string.IsNullOrEmpty(name) && TryGetByName(name, out var model) ? model : null;


        protected abstract ModelType FromEntity(EntityType entity);

        public bool TryAdd(EntityType entity)
        {
            var value = FromEntity(entity);

            return TryAdd(value.Id, value) && _modelNames.TryAdd(value.Name, value.Id);
        }

        public Task<bool> TryAdd(ModelType value)
        {
            var result = TryAdd(value.Id, value) && _modelNames.TryAdd(value.Name, value.Id);

            if (result)
            {
                AddToDb(value.ToEntity());
                Added?.Invoke(value);
            }

            return Task.FromResult(result);
        }

        public Task<bool> TryRemove(ModelType value)
        {
            var result = TryRemove(value.Id, out _) && _modelNames.TryRemove(value.Name, out _);

            if (result)
            {
                RemoveFromDb(value);
                Removed?.Invoke(value);
            }

            return Task.FromResult(result);
        }

        public async Task<bool> TryUpdate(UpdateType update)
        {
            var result = TryGetValue(update.Id, out var model);

            if (result)
            {
                model.Update(update);
                result &= await TryUpdate(model);
            }

            return result;
        }

        public Task<bool> TryUpdate(ModelType value)
        {
            var result = TryGetValue(value.Id, out var model);

            if (result)
            {
                UpdateInDb(value.ToEntity());
                Updated?.Invoke(value);
            }

            return Task.FromResult(result);
        }

        public bool TryGetByName(string name, out ModelType model)
        {
            model = null;

            return _modelNames.TryGetValue(name, out var id) && TryGetValue(id, out model);
        }
    }
}
