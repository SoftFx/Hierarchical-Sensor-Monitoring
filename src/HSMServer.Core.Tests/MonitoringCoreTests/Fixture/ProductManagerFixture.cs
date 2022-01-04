namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class ProductManagerFixture : DatabaseFixture
    {
        public override string DatabaseFolder => nameof(ProductManagerTests);
        public override int DatabaseCount => 1 << 5;
    }
}
