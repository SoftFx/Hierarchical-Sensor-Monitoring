using HSMServer.Core.DataLayer;


namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseAdapterManager
    {
        private static int _dbNumber;


        public string DatabaseFolder { get; }

        public DatabaseAdapter DatabaseAdapter { get; private set; }


        public DatabaseAdapterManager(string databaseFolder)
        {
            ++_dbNumber;

            DatabaseFolder = databaseFolder;
            DatabaseAdapter = new DatabaseAdapter(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{_dbNumber}",
                    MonitoringDatabaseName = $"MonitoringData{_dbNumber}",
                });
        }


        internal void ClearDatabase()
        {
            DatabaseAdapter.Dispose();
            DatabaseAdapter = null;
        }

        internal void AddTestProduct() =>
            DatabaseAdapter.AddProduct(TestProductsManager.TestProduct);
    }
}
