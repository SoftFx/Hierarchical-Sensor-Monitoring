using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    public sealed class ValidationFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(BaseSensorModelValidatorTests);
    }
}
