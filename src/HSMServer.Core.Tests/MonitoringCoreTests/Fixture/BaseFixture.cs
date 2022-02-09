using HSMServer.Core.Cache;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class BaseFixture<T> : IClassFixture<T> where T : DatabaseFixture
    {
        private protected readonly DatabaseAdapterManager _databaseAdapterManager;
        private protected readonly SensorValuesFactory _sensorValuesFactory;
        private protected readonly SensorValuesTester _sensorValuesTester;

        protected readonly ProductManager _productManager;
        protected readonly ValuesCache _valuesCache;

        protected MonitoringCore _monitoringCore;

        protected BaseFixture(DatabaseFixture fixture)
        {
            _databaseAdapterManager = new DatabaseAdapterManager(fixture.DatabasePath);
            _databaseAdapterManager.AddTestProduct();
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _sensorValuesFactory = new SensorValuesFactory(TestProductsManager.TestProduct.Key);
            _sensorValuesTester = new SensorValuesTester(TestProductsManager.TestProduct.Name);

            _valuesCache = new ValuesCache();

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseAdapterManager.DatabaseAdapter, productManagerLogger);
        }
    }
}
