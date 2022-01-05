namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class MonitoringDataReceiverFixture : DatabaseFixture
    {
        public override string DatabaseFolder => nameof(MonitoringDataReceiverTests);
        public override int DatabaseCount => 1 << 5; 
    }
}
