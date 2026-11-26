using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.DatabaseTests
{
    // Each class owns its database folder and its DatabaseRegisterFixture, so the database test
    // classes run in parallel with each other (#1943); the tests of one class still run in order.
    public abstract class DatabaseCoreTestsBase<T> : DatabaseTestClass, IClassFixture<T> where T : DatabaseFixture
    {
        protected readonly DatabaseCoreManager _databaseCoreManager;

        protected DatabaseCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            dbRegisterFixture.RegisterDatabase(_databaseCoreManager);
        }
    }
}
