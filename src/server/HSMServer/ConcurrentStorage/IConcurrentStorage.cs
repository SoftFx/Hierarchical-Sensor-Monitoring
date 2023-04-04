using System;
using System.Threading.Tasks;

namespace HSMServer.ConcurrentStorage
{
    public interface IConcurrentStorage<ModelType, EntityType, UpdateType>
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateModel
    {
        ModelType this[Guid id] { get; }

        ModelType this[string name] { get; }


        event Action<ModelType> Added;
        event Action<ModelType> Updated;
        event Action<ModelType> Removed;


        Task<bool> TryUpdate(UpdateType update);

        bool TryGetValue(Guid id, out ModelType model);

        Task Initialize();
    }
}
