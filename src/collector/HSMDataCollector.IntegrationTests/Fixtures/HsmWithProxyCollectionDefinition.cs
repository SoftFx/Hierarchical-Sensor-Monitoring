using Xunit;

namespace HSMDataCollector.IntegrationTests.Fixtures
{
    [CollectionDefinition("HSM Server with Proxy")]
    public class HsmWithProxyCollectionDefinition : ICollectionFixture<HsmServerWithProxyFixture>
    {
    }
}
