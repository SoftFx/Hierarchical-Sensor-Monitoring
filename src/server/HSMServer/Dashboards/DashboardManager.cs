using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed class DashboardManager : ConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>, IDashboardManager
    {
        private readonly IDashboardCollection _dbCollection;
        private readonly ITreeValuesCache _cache;


        protected override Action<DashboardEntity> AddToDb => _dbCollection.AddEntity;

        protected override Action<DashboardEntity> UpdateInDb => _dbCollection.UpdateEntity;

        protected override Action<Dashboard> RemoveFromDb => (dashboard) => _dbCollection.RemoveEntity(dashboard.Id);

        protected override Func<List<DashboardEntity>> GetFromDb => _dbCollection.ReadCollection;


        public DashboardManager(IDatabaseCore database, ITreeValuesCache cache)
        {
            _dbCollection = database.Dashboards;
            _cache = cache;

            Added += AddDashboardSubscriptions;
            Removed -= RemoveDashboardSubscriptions;
        }


        public Task<bool> TryAdd(DashboardAdd dashboardAdd, out Dashboard dashboard)
        {
            dashboard = new Dashboard(dashboardAdd);

            return TryAdd(dashboard);
        }

        protected override Dashboard FromEntity(DashboardEntity entity) => new(entity);


        private void AddDashboardSubscriptions(Dashboard board)
        {
            board.GetSensorModel += _cache.GetSensor;
        }

        private void RemoveDashboardSubscriptions(Dashboard board, InitiatorInfo _)
        {
            board.GetSensorModel -= _cache.GetSensor;
        }
    }
}