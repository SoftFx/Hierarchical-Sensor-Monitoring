using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    // An empty database: every test adds the root products it renames.
    public sealed class RootRenameFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(RootRenameTests);
    }
}
