using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly List<DatabaseCoreManager> _createdDatabases = new(1 << 8);


        public void Dispose()
        {
            var folders = _createdDatabases.Select(u => u.DatabaseFolder).Distinct();

            _createdDatabases.ForEach(db => db.ClearDatabase());

            foreach (var folder in folders)
                TryRemoveFolder(folder);
        }

        // One attempt, no retry. DatabaseCore.Dispose does not close the Dashboards (ServerLayout)
        // and ChangeSets databases, so their files stay locked until the test host exits and the
        // folder cannot be removed here; FileManager.SafeRemoveFolder would retry 5 x 5 s on every
        // such folder for nothing (#1943). What is left is removed by the DatabaseFixture of the
        // next run, when no process holds it any more.
        private static void TryRemoveFolder(string folder)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal void RegisterDatabase(DatabaseCoreManager dbAdapterManager) =>
            _createdDatabases.Add(dbAdapterManager);
    }
}
