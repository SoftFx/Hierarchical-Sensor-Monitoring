namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class MonitoringDataReceiverFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(MonitoringDataReceiverTests);
        protected override int DatabaseCount => 1 << 5; 
    }
}
