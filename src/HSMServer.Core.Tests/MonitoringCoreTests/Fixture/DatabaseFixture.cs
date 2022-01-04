﻿using HSMCommon;
using HSMServer.Core.Tests.MonitoringDataReceiverTests;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class DatabaseFixture
    {
        public abstract string DatabaseFolder { get; }
        public abstract int DatabaseCount { get; }

        internal List<DatabaseAdapterManager> CreatedDatabases { get; set; }

        public DatabaseFixture()
        {
            if (Directory.Exists(DatabaseFolder))
                DeleteDatabaseDirectory();

            CreatedDatabases = new List<DatabaseAdapterManager>(DatabaseCount);
        }

        public void Dispose()
        {
            CreatedDatabases.ForEach(db => db.ClearDatabase());
            DeleteDatabaseDirectory();
        }

        private void DeleteDatabaseDirectory() =>
            FileManager.SafeRemoveFolder(DatabaseFolder);
    }
}
