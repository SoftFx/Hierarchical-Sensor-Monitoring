using HSMCommon;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    [CollectionDefinition("Database collection")]
    public class DatabaseCollection : ICollectionFixture<DatabaseRegisterFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }


    public class DatabaseRegisterFixture : IDisposable
    {
        private readonly List<DatabaseAdapterManager> _createdDatabases = new(1 << 8);


        public void Dispose()
        {
            var folders = _createdDatabases.Select(u => u.DatabaseFolder);

            _createdDatabases.ForEach(db => db.ClearDatabase());

            foreach (var folder in folders)
                FileManager.SafeRemoveFolder(folder);
        }

        internal void RegisterDatabase(DatabaseAdapterManager dbAdapterManager) =>
            _createdDatabases.Add(dbAdapterManager);
    }
}
