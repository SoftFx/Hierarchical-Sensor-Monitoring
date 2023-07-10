namespace HSMServer.Core.Cache.UpdateEntities;

public interface IUpdateComparer<EntityType, UpdateType>
{
    string Compare(EntityType entity, UpdateType update);
}