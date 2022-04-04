using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;


namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseCoreManager
    {
        private static int _dbNumber;


        public string DatabaseFolder { get; }

        public IDatabaseCore DatabaseCore { get; private set; }


        public DatabaseCoreManager(string databaseFolder)
        {
            ++_dbNumber;

            DatabaseFolder = databaseFolder;
            DatabaseCore = new DatabaseCore(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{_dbNumber}",
                    MonitoringDatabaseName = $"MonitoringData{_dbNumber}",
                });
        }


        internal void ClearDatabase()
        {
            DatabaseCore.Dispose();
            DatabaseCore = null;
        }

        internal void AddTestProduct() =>
            DatabaseCore.AddProduct(TestProductsManager.TestProduct);
    }
}
