using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using System;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    public sealed class DatabaseCoreManager
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
                    EnvironmentDatabaseName = $"EnvironmentData{number}_{Environment.CurrentManagedThreadId}",
                    SensorValuesDatabaseName = $"SensorValues{number}_{Environment.CurrentManagedThreadId}",
                    ServerLayoutDatabaseName = $"ServerLayout{number}_{Environment.CurrentManagedThreadId}",
                    JournalValuesDatabaseName = $"JournalValues{number}_{Environment.CurrentManagedThreadId}",
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
