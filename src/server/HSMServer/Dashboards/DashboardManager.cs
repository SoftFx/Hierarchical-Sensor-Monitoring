using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;

namespace HSMServer.Dashboards
{
    public sealed class DashboardManager : ConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>, IDashboardManager
    {
        private readonly IDashboardCollection _dbCollection;


        protected override Action<DashboardEntity> AddToDb => _dbCollection.AddEntity;

        protected override Action<DashboardEntity> UpdateInDb => _dbCollection.UpdateEntity;

        protected override Action<Dashboard> RemoveFromDb => (dashboard) => _dbCollection.RemoveEntity(dashboard.Id);

        protected override Func<List<DashboardEntity>> GetFromDb => _dbCollection.ReadCollection;


        public DashboardManager(IDatabaseCore database)
        {
            _dbCollection = database.Dashboards;
        }


        protected override Dashboard FromEntity(DashboardEntity entity) => new(entity);
    }
}