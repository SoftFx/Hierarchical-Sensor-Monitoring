using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public sealed class SensorValuesHistoryFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(SensorValuesHistoryTests);
    }
}
