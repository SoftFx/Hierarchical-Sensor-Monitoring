using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    public sealed class AccessKeyFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(AccessKeyTests);
    }
}
