using Xunit;

namespace HSMDataCollector.IntegrationTests.Fixtures
{
    [CollectionDefinition("HSM Server")]
    public class HsmCollectionDefinition : ICollectionFixture<HsmServerFixture>
    {
    }
}
