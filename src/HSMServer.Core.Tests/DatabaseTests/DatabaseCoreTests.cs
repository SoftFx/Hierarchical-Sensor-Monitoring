using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.DatabaseTests.Fixture;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests
{
    public class DatabaseCoreTests : DatabaseCoreTestsBase<DatabaseCoreFixture>
    {
        private IDatabaseCore _databaseCore;

        public DatabaseCoreTests(DatabaseCoreFixture fixture, DatabaseRegisterFixture registerFixture) 
            : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
        }
    }
}
