using System;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverFixture : IDisposable
    {
        internal List<DatabaseAdapterManager> CreatedDatabases { get; } =
            new List<DatabaseAdapterManager>();


        public MonitoringDataReceiverFixture()
        {
            if (Directory.Exists(DatabaseAdapterManager.DatabaseFolder))
                DeleteDatabaseDirectory();
        }


        public void Dispose()
        {
            CreatedDatabases.ForEach(db => db.ClearDatabase());
            DeleteDatabaseDirectory();
        }

        private static void DeleteDatabaseDirectory() =>
            Directory.Delete(DatabaseAdapterManager.DatabaseFolder, true);
    }
}
