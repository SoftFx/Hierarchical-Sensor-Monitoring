using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Dashboards
{
    public interface IDashboardManager : IConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>
    {
    }
}