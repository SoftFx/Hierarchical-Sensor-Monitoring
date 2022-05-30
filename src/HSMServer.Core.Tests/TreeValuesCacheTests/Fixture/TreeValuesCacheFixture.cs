using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public sealed class TreeValuesCacheFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TreeValuesCacheTests);
    }
}
