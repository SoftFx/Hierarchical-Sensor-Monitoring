using HSMServer.Core.DataLayer;
using System.Threading;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseAdapterManager
    {
        private static int _dbNumber;


        public string DatabaseFolder { get; }

        public DatabaseAdapter DatabaseAdapter { get; private set; }


        public DatabaseAdapterManager(string databaseFolder)
        {
            var number = Interlocked.Increment(ref _dbNumber);

            DatabaseFolder = databaseFolder;
            DatabaseAdapter = new DatabaseAdapter(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{number}_{Thread.CurrentThread.ManagedThreadId}",
                    MonitoringDatabaseName = $"MonitoringData{number}_{Thread.CurrentThread.ManagedThreadId}",
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
