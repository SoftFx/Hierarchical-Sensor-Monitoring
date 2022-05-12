using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
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
        private protected readonly SensorValuesFactory _sensorValuesFactory;
        private protected readonly SensorValuesTester _sensorValuesTester;

        protected readonly ProductManager _productManager;
        protected readonly TreeValuesCache _valuesCache;
        protected readonly IUpdatesQueue _updatesQueue;

        protected MonitoringCore _monitoringCore;


        protected MonitoringCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            _databaseCoreManager.AddTestProduct();
            dbRegisterFixture.RegisterDatabase(_databaseCoreManager);

            _sensorValuesFactory = new SensorValuesFactory(TestProductsManager.TestProduct.Id);
            _sensorValuesTester = new SensorValuesTester(TestProductsManager.TestProduct.DisplayName);

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseCoreManager.DatabaseCore, productManagerLogger);

            var userManagerLogger = CommonMoqs.CreateNullLogger<UserManager>();
            var userManager = new UserManager(_databaseCoreManager.DatabaseCore, userManagerLogger);
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, userManager);

            _updatesQueue = new Mock<IUpdatesQueue>().Object;
        }
    }
}
