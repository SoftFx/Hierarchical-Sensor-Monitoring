using HSMServer.Core.Cache;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class BaseFixture<T> : IClassFixture<T> where T : DatabaseFixture
    {
        private protected DatabaseAdapterManager _databaseAdapterManager;
        private protected SensorValuesFactory _sensorValuesFactory;
        private protected SensorValuesTester _sensorValuesTester;

        protected MonitoringCore _monitoringCore;
        protected ProductManager _productManager;
        protected ValuesCache _valuesCache;


        protected BaseFixture(DatabaseFixture fixture)
        {
            _databaseAdapterManager = new DatabaseAdapterManager(fixture.DatabasePath);
            _databaseAdapterManager.AddTestProduct();
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _sensorValuesFactory = new SensorValuesFactory(_databaseAdapterManager);
            _sensorValuesTester = new SensorValuesTester(_databaseAdapterManager);

            _valuesCache = new ValuesCache();

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseAdapterManager.DatabaseAdapter, productManagerLogger);
        }
    }
}
