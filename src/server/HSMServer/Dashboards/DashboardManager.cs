using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMServer.Model.Dashboards;

namespace HSMServer.Dashboards
{
    public sealed class DashboardManager : ConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>, IDashboardManager
    {
        private readonly IDashboardCollection _dbCollection;
        private readonly ConcurrentDictionary<Guid, DashboardViewModel> _dashboardViewModels = new();

        protected override Action<DashboardEntity> AddToDb => _dbCollection.AddEntity;

        protected override Action<DashboardEntity> UpdateInDb => _dbCollection.UpdateEntity;

        protected override Action<Dashboard> RemoveFromDb => (dashboard) => _dbCollection.RemoveEntity(dashboard.Id);

        protected override Func<List<DashboardEntity>> GetFromDb => _dbCollection.ReadCollection;


        public DashboardManager(IDatabaseCore database)
        {
            _dbCollection = database.Dashboards;
        }


        public Task<bool> TryAdd(DashboardAdd dashboardAdd, out Dashboard dashboard)
        {
            dashboard = new Dashboard(dashboardAdd);

            return TryAdd(dashboard);
        }

        protected override Dashboard FromEntity(DashboardEntity entity) => new(entity);
    }
}