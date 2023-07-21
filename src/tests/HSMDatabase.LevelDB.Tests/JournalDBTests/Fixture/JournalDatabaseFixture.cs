using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMDatabase.LevelDB.Tests.JournalDBTests;

public class JournalDatabaseFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(JournalCacheTests);
}