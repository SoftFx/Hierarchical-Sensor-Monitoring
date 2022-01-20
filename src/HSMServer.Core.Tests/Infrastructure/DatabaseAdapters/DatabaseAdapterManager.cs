using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseAdapterManager
    {
        private static int _dbNumber;

        public DatabaseAdapter DatabaseAdapter { get; private set; }


        public DatabaseAdapterManager(string databaseFolder)
        {
            ++_dbNumber;

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
    }
}
