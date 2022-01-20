namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class ProductManagerFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ProductManagerTests);
        protected override int DatabaseCount => 1 << 5;
    }
}
