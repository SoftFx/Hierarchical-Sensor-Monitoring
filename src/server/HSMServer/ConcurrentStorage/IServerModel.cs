using System;

namespace HSMServer.ConcurrentStorage
{
    public interface IServerModel<EntityType, UpdateType>
    {
        Guid Id { get; }

        string DisplayName { get; }


        EntityType ToEntity();

        void Update(UpdateType update);
    }
}
