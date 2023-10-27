using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using System;

namespace HSMServer.Dashboards
{
    public sealed class Dashboard : IServerModel<DashboardEntity, DashboardUpdate>
    {
        public Guid Id => throw new NotImplementedException();

        public string Name => throw new NotImplementedException();


        public DashboardEntity ToEntity()
        {
            throw new NotImplementedException();
        }

        public void Update(DashboardUpdate update)
        {
            throw new NotImplementedException();
        }
    }
}
