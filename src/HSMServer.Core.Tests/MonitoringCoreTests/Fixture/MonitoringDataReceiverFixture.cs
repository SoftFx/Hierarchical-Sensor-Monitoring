namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class MonitoringDataReceiverFixture : DatabaseFixture
    {
        public override string DatabaseFolder { get => nameof(MonitoringDataReceiverTests); }
        public override int DatabaseCount { get => 1 << 5; }
    }
}
