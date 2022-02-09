namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class SensorsFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(SensorsTests);
        protected override int DatabaseCount => 1 << 5;
    }
}
