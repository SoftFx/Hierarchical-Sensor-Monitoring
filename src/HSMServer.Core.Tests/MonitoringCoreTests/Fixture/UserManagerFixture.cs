namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public class UserManagerFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(UserManagerFixture);
        protected override int DatabaseCount => 1 << 5;
    }
}
