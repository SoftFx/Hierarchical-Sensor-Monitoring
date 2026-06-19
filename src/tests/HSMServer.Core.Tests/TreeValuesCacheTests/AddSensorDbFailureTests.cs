using System;
using System.Collections.Generic;
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


        // Regression for #1128: when _database.AddSensor throws, the sensor must NOT
        // be left in _sensorsById/_cache/parentProduct.Sensors OR in the DB, and a
        // retry with the same path must succeed with a fresh Guid.
        [Fact]
        [Trait("Category", "Add new sensor value")]
        public async Task AddSensor_WhenDbAddSensorFails_SensorNotInCacheOrDbAndRetrySucceeds()
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

            AssertSensorNotPresent(productId, sensorPath, product, initialSensorCount);

            _failProductId = Guid.Empty;

            var retryValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(accessKey, productId, retryValue);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, sensorPath, out _),
                "Retry with the same path must succeed once the DB is healthy.");
            Assert.Equal(initialSensorCount + 1, product.Sensors.Count);
        }

        // Regression for #1128 follow-up: a failure AFTER _database.AddSensor returns
        // (here: a ChangeSensorEvent subscriber throwing on Add) must still roll the row
        // back AND fire the matching ChangeSensorEvent(Delete) so subscribers that saw
        // Add can clean up rather than hold a dangling reference. Without the Delete,
        // the DB has the row removed while the in-memory cache says the sensor doesn't
        // exist AND subscribers think it still does.
        [Fact]
        [Trait("Category", "Add new sensor value")]
        public async Task AddSensor_WhenPostPersistStepFails_RollsBackAndFiresDeleteEvent()
        {
            var accessKey = Guid.Parse(TestProductsManager.TestProductKey.Id);
            var productId = TestProductsManager.ProductId;

            var product = _valuesCache.GetProduct(productId);
            var initialSensorCount = product.Sensors.Count;

            const string sensorPath = "post_persist_failure_sensor";

            var recordedEvents = new List<(string Name, ActionType Type)>();
            void RecordingHandler(BaseSensorModel s, ActionType t)
            {
                if (s.DisplayName == sensorPath)
                    recordedEvents.Add((s.DisplayName, t));
            }

            void ThrowingHandler(BaseSensorModel s, ActionType t)
            {
                if (s.DisplayName == sensorPath && t == ActionType.Add)
                    throw new InvalidOperationException("Simulated post-persist subscriber failure");
            }

            // Subscribe the recorder first so it sees Add before ThrowingHandler throws.
            _valuesCache.ChangeSensorEvent += RecordingHandler;
            _valuesCache.ChangeSensorEvent += ThrowingHandler;
            try
            {
                var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
                await _valuesCache.AddSensorValueAsync(accessKey, productId, value);
                await Task.Delay(200);

                // Even though _database.AddSensor succeeded, the post-persist throw must
                // roll the row back so cache and DB stay consistent.
                AssertSensorNotPresent(productId, sensorPath, product, initialSensorCount);
            }
            finally
            {
                _valuesCache.ChangeSensorEvent -= ThrowingHandler;
                _valuesCache.ChangeSensorEvent -= RecordingHandler;
            }

            // Subscribers that saw Add must also see Delete during the rollback —
            // otherwise they retain a dangling reference to a sensor that's now gone
            // from cache and DB.
            Assert.Equal(new[] { (sensorPath, ActionType.Add), (sensorPath, ActionType.Delete) }, recordedEvents);

            // Retry with the same path now that the failing subscriber is gone: must succeed.
            var retryValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(accessKey, productId, retryValue);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, sensorPath, out _),
                "Retry with the same path must succeed once the post-persist failure is gone.");
            Assert.Equal(initialSensorCount + 1, product.Sensors.Count);
        }


        private void AssertSensorNotPresent(Guid productId, string sensorPath, ProductModel product, int initialSensorCount)
        {
            Assert.False(_valuesCache.TryGetSensorByPath(productId, sensorPath, out _),
                "Sensor must not be visible in cache after the failure.");
            Assert.Equal(initialSensorCount, product.Sensors.Count);

            var dbSensorsAfterFailure = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .Where(s => Guid.TryParse(s.ProductId, out var pid) && pid == productId)
                .ToList();
            Assert.DoesNotContain(dbSensorsAfterFailure, s => s.DisplayName == sensorPath);
        }
    }
}
