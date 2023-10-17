using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace HSMServer.ConcurrentStorage
{
    public abstract class ConcurrentStorageNames<ModelType, EntityType, UpdateType> : ConcurrentStorage<ModelType, EntityType, UpdateType>
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateModel
    {
        private readonly ConcurrentDictionary<string, Guid> _modelNames = new();


        public ModelType this[string name] => !string.IsNullOrEmpty(name) &&
            TryGetIdByName(name, out var id) && TryGetValue(id, out var model) ? model : null;


        public bool TryGetIdByName(string name, out Guid id) => _modelNames.TryGetValue(name, out id);

        public override bool TryAdd(EntityType entity, out ModelType model) =>
            base.TryAdd(entity, out model) && _modelNames.TryAdd(model.Name, model.Id);

        public override Task<bool> TryAdd(ModelType model) => _modelNames.TryAdd(model.Name, model.Id)
            ? base.TryAdd(model)
            : Task.FromResult(false);

        public override async Task<bool> TryUpdate(UpdateType update)
        {
            var result = TryGetValue(update.Id, out var model);

            if (result)
            {
                if (update.Name != null && _modelNames.TryRemove(model.Name, out var id))
                    _modelNames.TryAdd(update.Name, id);

                result &= await base.TryUpdate(update);
            }

            return result;
        }

        public override Task<bool> TryRemove(RemoveModel remove)=>
            TryGetValue(remove.Id, out var model) && _modelNames.TryRemove(model.Name, out _)
                ? base.TryRemove(remove)
                : Task.FromResult(false);
    }
}
