using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMDatabase.LevelDB.Tests.JournalDBTests;

public class JournalPageOrderFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(JournalPageOrderTests);
}
