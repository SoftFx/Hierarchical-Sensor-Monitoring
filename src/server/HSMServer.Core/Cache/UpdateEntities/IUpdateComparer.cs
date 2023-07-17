namespace HSMServer.Core.Cache.UpdateEntities;

public interface IUpdateComparer<EntityType, UpdateType>
{
    bool Compare(EntityType entity, UpdateType update, out string message);
}