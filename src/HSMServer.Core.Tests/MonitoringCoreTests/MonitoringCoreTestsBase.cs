using HSMServer.Core.Cache;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    [Collection("Database collection")]
    public abstract class MonitoringCoreTestsBase<T> : IClassFixture<T> where T : DatabaseFixture
    {
        private protected readonly DatabaseCoreManager _databaseAdapterManager;
        private protected readonly SensorValuesFactory _sensorValuesFactory;
        private protected readonly SensorValuesTester _sensorValuesTester;

        protected readonly ProductManager _productManager;
        protected readonly ValuesCache _valuesCache;

        protected MonitoringCore _monitoringCore;


        protected MonitoringCoreTestsBase(DatabaseFixture fixture, DatabaseRegisterFixture dbRegisterFixture)
        {
            _databaseAdapterManager = new DatabaseCoreManager(fixture.DatabasePath);
            _databaseAdapterManager.AddTestProduct();

            dbRegisterFixture.RegisterDatabase(_databaseAdapterManager);

            _sensorValuesFactory = new SensorValuesFactory(TestProductsManager.TestProduct.Key);
            _sensorValuesTester = new SensorValuesTester(TestProductsManager.TestProduct.Name);

            _valuesCache = new ValuesCache();

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseAdapterManager.DatabaseCore, productManagerLogger);
        }
    }
}
