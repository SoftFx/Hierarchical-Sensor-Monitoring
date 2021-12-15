using System;
using System.Collections.Generic;
using System.IO;
using HSMCommon;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverFixture : IDisposable
    {
        internal List<DatabaseAdapterManager> CreatedDatabases { get; } =
            new List<DatabaseAdapterManager>(1 << 5);


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
            FileManager.SafeRemoveFolder(DatabaseAdapterManager.DatabaseFolder);
    }
}
