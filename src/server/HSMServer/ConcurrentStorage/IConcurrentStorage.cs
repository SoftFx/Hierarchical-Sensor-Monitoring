using System;
using System.Threading.Tasks;

namespace HSMServer.ConcurrentStorage
{
    public interface IConcurrentStorage<ModelType, EntityType, UpdateType> : IDisposable
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateModel
    {
        ModelType this[Guid id] { get; }

        ModelType this[Guid? id] { get; }

        ModelType this[string name] { get; }


        event Action<ModelType> Added;
        event Action<ModelType> Updated;
        event Action<ModelType> Removed;


        Task<bool> TryUpdate(UpdateType update);

        Task<bool> TryUpdate(ModelType value);

        Task<bool> TryRemove(Guid folderId);

        bool TryGetValue(Guid id, out ModelType model);

        bool TryGetValueById(Guid? id, out ModelType model);

        Task Initialize();
    }
}
