using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : BaseServerModel<DashboardEntity, DashboardUpdate>
    {
        internal Dashboard(DashboardEntity entity) : base(entity)
        {
        }

        internal Dashboard(DashboardAdd addModel) : base(addModel) { }
    }
}