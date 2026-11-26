using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    // Gives every database test class (DatabaseCoreTestsBase, MonitoringCoreTestsBase) its own
    // DatabaseRegisterFixture, so the classes no longer share one serial collection and run in
    // parallel (#1943). A non-generic root class because a generic base cannot itself implement
    // both IClassFixture<T> and IClassFixture<DatabaseRegisterFixture> (CS0695); xUnit reads the
    // class fixtures from every interface a test class implements, inherited ones included.
    public abstract class DatabaseTestClass : IClassFixture<DatabaseRegisterFixture> { }


    // Closes the databases one test class opened and removes their folder when the class ends.
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
