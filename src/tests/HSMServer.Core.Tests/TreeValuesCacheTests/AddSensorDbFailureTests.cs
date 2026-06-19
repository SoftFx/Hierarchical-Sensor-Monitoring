using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    [Collection("Database collection")]
    public class AddSensorDbFailureTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>
    {
        private Guid _failProductId = Guid.Empty;


        public AddSensorDbFailureTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner)
        {
            return new FailingDatabaseCore(inner, entity =>
                _failProductId != Guid.Empty &&
                Guid.TryParse(entity.ProductId, out var pid) &&
                pid == _failProductId);
        }


        // Regression for #1128: when _database.AddSensor throws inside AddSensor,
        // the sensor must NOT be left in _sensorsById/_cache/parentProduct.Sensors,
        // and a retry with the same path must succeed with a fresh Guid.
        [Fact]
        [Trait("Category", "Add new sensor value")]
        public async Task AddSensor_WhenDbAddSensorFails_SensorNotInCacheAndRetrySucceeds()
        {
            var accessKey = Guid.Parse(TestProductsManager.TestProductKey.Id);
            var productId = TestProductsManager.ProductId;

            var product = _valuesCache.GetProduct(productId);
            var initialSensorCount = product.Sensors.Count;

            const string sensorPath = "db_failure_sensor";
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);

            _failProductId = productId;

            await _valuesCache.AddSensorValueAsync(accessKey, productId, value);
            await Task.Delay(200);

            // The retry must be able to use the same path without colliding with orphan state.
            Assert.False(_valuesCache.TryGetSensorByPath(productId, sensorPath, out _),
                "Sensor must not be visible in cache after DB AddSensor failure.");
            Assert.Equal(initialSensorCount, product.Sensors.Count);

            var dbSensorsAfterFailure = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .Where(s => Guid.TryParse(s.ProductId, out var pid) && pid == productId)
                .ToList();
            Assert.DoesNotContain(dbSensorsAfterFailure, s => s.DisplayName == sensorPath);

            _failProductId = Guid.Empty;

            var retryValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(accessKey, productId, retryValue);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensorAfterRetry),
                "Retry with the same path must succeed once the DB is healthy.");
            Assert.Equal(initialSensorCount + 1, product.Sensors.Count);
        }
    }
}
