namespace HSMServer.Core.Cache.UpdateEntities;

public interface IUpdateComparer<EntityType, UpdateType>
{
    string GetComparisonString(EntityType entity, UpdateType update);
}