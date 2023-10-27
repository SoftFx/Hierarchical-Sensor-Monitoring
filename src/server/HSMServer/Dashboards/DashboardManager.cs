using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;

namespace HSMServer.Dashboards
{
    public sealed class DashboardManager : ConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>, IDashboardManager
    {
        protected override Action<DashboardEntity> AddToDb => throw new NotImplementedException();

        protected override Action<DashboardEntity> UpdateInDb => throw new NotImplementedException();

        protected override Action<Dashboard> RemoveFromDb => throw new NotImplementedException();

        protected override Func<List<DashboardEntity>> GetFromDb => throw new NotImplementedException();


        public DashboardManager(IDatabaseCore database)
        {

        }


        protected override Dashboard FromEntity(DashboardEntity entity) => new(entity);
    }
}