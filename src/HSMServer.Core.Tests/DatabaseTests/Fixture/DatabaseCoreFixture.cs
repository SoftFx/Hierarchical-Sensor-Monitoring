using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.DatabaseTests.Fixture
{
    public class DatabaseCoreFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(DatabaseCoreTests);
    }
}
