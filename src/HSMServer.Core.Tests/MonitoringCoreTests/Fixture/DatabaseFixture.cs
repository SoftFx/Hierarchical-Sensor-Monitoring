using HSMCommon;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class DatabaseFixture : IDisposable
    {
        protected abstract string DatabaseFolder { get; }
        protected abstract int DatabaseCount { get; }

        internal string DatabasePath => $"TestDB_{DatabaseFolder}";

        internal List<DatabaseAdapterManager> CreatedDatabases { get; }

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
            FileManager.SafeRemoveFolder(DatabasePath);
    }
}
