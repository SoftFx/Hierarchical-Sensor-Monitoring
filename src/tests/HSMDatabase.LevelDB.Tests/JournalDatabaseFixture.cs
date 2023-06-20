using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMDatabase.LevelDB.Tests;

public class JournalDatabaseFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(JournalDatabaseFixture);
}