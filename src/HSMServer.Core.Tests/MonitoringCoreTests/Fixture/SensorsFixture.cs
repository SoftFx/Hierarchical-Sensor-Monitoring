using HSMServer.Core.Tests.Infrastructure;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class SensorsFixture : DatabaseFixture
    {
        public override string DatabaseFolder => nameof(SensorsTests);
        public override int DatabaseCount => 1 << 5;
    }
}
