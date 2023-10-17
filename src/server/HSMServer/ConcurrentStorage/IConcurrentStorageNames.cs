using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IConcurrentStorageNames<ModelType, EntityType, UpdateType> : IConcurrentStorage<ModelType, EntityType, UpdateType>, IDisposable
        where ModelType : class, IServerModel<EntityType, UpdateType>
        where UpdateType : IUpdateRequest
    {
        ModelType this[string name] { get; }
    }
}
