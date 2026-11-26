using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Dashboards
{
    public sealed class DashboardManager : ConcurrentStorage<Dashboard, DashboardEntity, DashboardUpdate>, IDashboardManager
    {
        private readonly IDashboardCollection _dbCollection;
        private readonly ITreeValuesCache _cache;
        private readonly object _storeLock = new();


        protected override Action<DashboardEntity> AddToDb => _dbCollection.AddEntity;

        protected override Action<DashboardEntity> UpdateInDb => _dbCollection.UpdateEntity;

        protected override Action<Dashboard> RemoveFromDb => (dashboard) => _dbCollection.RemoveEntity(dashboard.Id);

        protected override Func<List<DashboardEntity>> GetFromDb => _dbCollection.ReadCollection;


        public DashboardManager(IDatabaseCore database, ITreeValuesCache cache)
        {
            _dbCollection = database.Dashboards;
            _cache = cache;

            Added += AddDashboardSubscriptions;
        }


        public Task<bool> TryAdd(DashboardAdd dashboardAdd, out Dashboard dashboard)
        {
            dashboard = new Dashboard(dashboardAdd, _cache);

            return TryAdd(dashboard);
        }

        // A dashboard is stored whole, as a snapshot, from request threads (a
        // user's edit) and from the cache's thread (a source a panel subscription
        // adds). The snapshot and its write are one step: otherwise an older
        // snapshot that is written later replaces a newer one in the store.
        public override Task<bool> TryUpdate(Dashboard value)
        {
            lock (_storeLock)
                return base.TryUpdate(value);
        }

        protected override Dashboard FromEntity(DashboardEntity entity) => new(entity, _cache);


        private void AddDashboardSubscriptions(Dashboard board)
        {
            void CallUpdate() => TryUpdate(board);

            board.UpdatedEvent += CallUpdate;
        }
    }
}