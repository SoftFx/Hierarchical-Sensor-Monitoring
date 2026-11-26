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


    // Closes the databases one test class opened and removes their folder when the class ends;
    // a folder that cannot be removed fails the class's cleanup.
    public class DatabaseRegisterFixture : IDisposable
    {
        private readonly List<DatabaseCoreManager> _createdDatabases = new(1 << 8);


        public void Dispose()
        {
            var folders = _createdDatabases.Select(u => u.DatabaseFolder).Distinct().ToList();

            _createdDatabases.ForEach(db => db.ClearDatabase());

            var failures = new List<Exception>();

            foreach (var folder in folders)
                try
                {
                    RemoveFolder(folder);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }

            if (failures.Count > 0)
                throw new AggregateException("A test class left its database folder behind: something still holds a file in it.", failures);
        }

        // One attempt, no retry, and a failure is an error of this class: DatabaseCore.Dispose closes
        // every database it opened, so nothing may hold a file of the folder any more (#1943 follow-up).
        // Throwing from a class fixture's Dispose makes xUnit report a "Test Class Cleanup Failure" on
        // this class alone and turn the run red, while the class's tests keep their own results.
        private static void RemoveFolder(string folder)
        {
            if (!Directory.Exists(folder))
                return;

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Could not remove the test database folder {Path.GetFullPath(folder)}: {ex.Message}", ex);
            }
        }

        internal void RegisterDatabase(DatabaseCoreManager dbAdapterManager) =>
            _createdDatabases.Add(dbAdapterManager);
    }
}
