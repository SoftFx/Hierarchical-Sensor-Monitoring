using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    [Collection("Database collection")]
    public abstract class MonitoringCoreTestsBase<T> : IClassFixture<T> where T : DatabaseFixture
    {
        private protected readonly DatabaseCoreManager _databaseCoreManager;
        private protected readonly ApiSensorValuesFactory _sensorValuesFactory;

        protected readonly IUserManager _userManager;
        protected readonly IUpdatesQueue _updatesQueue;


        protected MonitoringCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture, bool addTestProduct = true)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            if (addTestProduct)
                _databaseCoreManager.AddTestProduct();
            dbRegisterFixture.RegisterDatabase(_databaseCoreManager);

            _sensorValuesFactory = new ApiSensorValuesFactory(addTestProduct ? TestProductsManager.TestProduct.Id : CommonConstants.SelfMonitoringProductKey);

            var userManagerLogger = CommonMoqs.CreateNullLogger<UserManager>();
            _userManager = new UserManager(_databaseCoreManager.DatabaseCore, userManagerLogger);

            _updatesQueue = new Mock<IUpdatesQueue>().Object;
        }
    }
}
