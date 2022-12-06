using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using System;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseCoreManager
    {
        private static int _dbNumber;


        public string DatabaseFolder { get; }

        public IDatabaseCore DatabaseCore { get; private set; }


        public DatabaseCoreManager(string databaseFolder)
        {
            var number = Interlocked.Increment(ref _dbNumber);

            DatabaseFolder = databaseFolder;
            DatabaseCore = new DatabaseCore(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{number}_{Thread.CurrentThread.ManagedThreadId}",
                    MonitoringDatabaseName = $"MonitoringData{number}_{Thread.CurrentThread.ManagedThreadId}",
                    SensorValuesDatabaseName = $"SensorValues{number}_{Thread.CurrentThread.ManagedThreadId}",
                });
        }


        internal void ClearDatabase()
        {
            DatabaseCore.Dispose();
            DatabaseCore = null;
        }

        internal void AddTestProduct()
        {
            DatabaseCore.AddProduct(TestProductsManager.TestProduct);
            DatabaseCore.AddAccessKey(TestProductsManager.TestProductKey);
        }

        internal ProductEntity GetProduct(Guid id) => DatabaseCore.GetProduct(id.ToString());
    }
}
