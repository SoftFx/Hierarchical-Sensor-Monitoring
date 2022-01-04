namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class ProductManagerFixture : DatabaseFixture
    {
        public override string DatabaseFolder { get => nameof(ProductManagerTests); }
        public override int DatabaseCount { get => 1 << 5; }
    }
}
