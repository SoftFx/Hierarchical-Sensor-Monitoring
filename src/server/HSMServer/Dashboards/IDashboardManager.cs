using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public interface IDashboardManager : IConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>
    {
        Task<bool> TryAdd(DashboardAdd dashboardAdd, out Dashboard dashboard);
    }
}