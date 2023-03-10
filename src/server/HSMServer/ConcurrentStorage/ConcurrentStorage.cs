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


        public event Action<ModelType> AddEvent;
        public event Action<ModelType> UpdateEvent;
        public event Action<ModelType> RemoveEvent;


        internal ModelType this[string name] =>
            !string.IsNullOrEmpty(name) && _modelNames.TryGetValue(name, out var id) && TryGetValue(id, out var model) ? model : null;


        public Task<bool> TryAdd(ModelType value)
        {
            var result = TryAdd(value.Id, value) && _modelNames.TryAdd(value.DisplayName, value.Id);

            if (result)
            {
                AddToDb(value.ToEntity());
                AddEvent?.Invoke(value);
            }

            return Task.FromResult(result);
        }

        internal Task<bool> TryRemove(ModelType value)
        {
            var result = TryRemove(value.Id, out _) && _modelNames.TryRemove(value.DisplayName, out _);

            if (result)
            {
                RemoveFromDb(value);
                RemoveEvent?.Invoke(value);
            }

            return Task.FromResult(result);
        }

        internal Task<bool> TryUpdate(UpdateType update)
        {
            var result = TryGetValue(update.Id, out var model);

            if (result)
            {
                model.Update(update);
                Update(model);
            }

            return Task.FromResult(result);
        }

        internal void Update(ModelType value)
        {
            UpdateInDb(value.ToEntity());
            UpdateEvent?.Invoke(value);
        }

        internal bool TryGet(string name, out ModelType model)
        {
            model = null;

            return _modelNames.TryGetValue(name, out var id) && TryGetValue(id, out model);
        }
    }
}
