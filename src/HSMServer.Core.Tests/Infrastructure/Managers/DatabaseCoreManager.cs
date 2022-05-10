using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
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
                });
        }


        internal void ClearDatabase()
        {
            DatabaseCore.Dispose();
            DatabaseCore = null;
        }
    }
}
