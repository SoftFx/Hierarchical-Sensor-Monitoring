using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;
using ApiValues = HSMSensorDataObjects.SensorValueRequests;

namespace HSMServer.Core.Tests.TableSensors
{
    public sealed class TableFeedSensorStoreFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableFeedSensorStoreTests);
    }


    /// <summary>
    /// The keyless value path of the feed sensors (#1847) against a store that loses a sensor's row, and a caller that
    /// stops waiting: the sensor is created only when its row was written, and a cancelled caller gets its own answer.
    /// </summary>
    [Collection("Database collection")]
    public class TableFeedSensorStoreTests : MonitoringCoreTestsBase<TableFeedSensorStoreFixture>
    {
        private static readonly DateTime T0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private readonly Guid _product = TestProductsManager.ProductId;
        private FailingDatabaseCore _database;


        public TableFeedSensorStoreTests(TableFeedSensorStoreFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _database = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task A_sensor_whose_row_the_store_lost_is_not_created_and_the_next_value_creates_it()
        {
            var path = $".tables/s{Guid.NewGuid():N}"[..20];
            IServerSensorValues values = new ServerSensorValues(_valuesCache);

            _database.ShouldLoseSensorAdd = _ => true;
            var lost = await values.AddServerValuesAsync(_product, [Value(path, 1)]);

            Assert.True(lost.ContainsKey(path));
            Assert.False(_valuesCache.TryGetSensorByPath(_product, path, out _));

            _database.ShouldLoseSensorAdd = null;
            Assert.Empty(await values.AddServerValuesAsync(_product, [Value(path, 2)]));
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            Assert.Equal(2, Assert.IsType<HSMCommon.Model.IntegerValue>(sensor.LastValue).Value);
        }

        [Fact]
        public async Task A_caller_that_stops_waiting_gets_an_answer_of_its_own()
        {
            IServerSensorValues values = new ServerSensorValues(_valuesCache);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            var answer = await values.AddServerValuesAsync(_product, [Value(".tables/c", 1)], cancelled.Token);

            Assert.Single(answer);
            Assert.True(answer.ContainsKey(_product.ToString()));
        }


        private static ServerSensorValue Value(string path, int value) =>
            new(new ApiValues.IntSensorValue { Path = path, Value = value, Time = T0.AddSeconds(value) }, "d", Unit.Count, "test");
    }
}
