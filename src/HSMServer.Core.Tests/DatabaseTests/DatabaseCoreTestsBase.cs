using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.DatabaseTests
{
    public abstract class DatabaseCoreTestsBase<T> : IClassFixture<T> where T : DatabaseFixture
    {
        private protected readonly DatabaseCoreManager _databaseCoreManager;

        protected DatabaseCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            dbRegisterFixture.RegisterDatabase(_databaseCoreManager);
        }
    }
}
