using System;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager;
using HSMCommon.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.FileSensors;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    public sealed class FileSensorPageDatabaseFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(FileSensorPageDatabaseTests);
    }


    // #1886: the reads of the sensor page over the real cache and the weekly databases: a value at the very first tick
    // of a storage week is found by its time and listed on the page that reaches it.
    public class FileSensorPageDatabaseTests : MonitoringCoreTestsBase<FileSensorPageDatabaseFixture>
    {
        private readonly Guid _product = TestProductsManager.ProductId;


        public FileSensorPageDatabaseTests(FileSensorPageDatabaseFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Fact]
        public async Task A_value_at_the_first_tick_of_a_week_is_found_by_its_time_and_listed()
        {
            const string path = "files/week";
            var weekStart = StorageWeek.GetStart(DateTime.UtcNow);

            foreach (var time in new[] { weekStart.AddMinutes(-2), weekStart.AddMinutes(-1), weekStart, weekStart.AddMinutes(1) })
            {
                var value = SensorValuesFactory.BuildSensorValue(SensorType.File, path, time);
                Assert.True((await _valuesCache.AddSensorValueAsync(Guid.Parse(TestProductsManager.TestProductKey.Id), _product, value)).IsOk);
            }

            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));

            var service = new FileSensorPageService(_valuesCache);

            var found = await service.GetFileAsync(sensor.Id, weekStart.Ticks);
            Assert.False(found.Busy);
            Assert.NotNull(found.Value);
            Assert.Equal(weekStart.Ticks, found.Value.Time.Ticks);

            // The page before the newest value reaches back over the start of the week.
            var page = await service.GetPageAsync(sensor.Id, "id", weekStart.AddMinutes(1).Ticks, user: null);
            Assert.Equal(new[] { weekStart, weekStart.AddMinutes(-1), weekStart.AddMinutes(-2) }.Select(t => t.Ticks), page.Rows.Select(r => r.TimeTicks));

            // And the next page starts after the value at the start of the week, which it does not repeat.
            var older = await service.GetPageAsync(sensor.Id, "id", weekStart.Ticks, user: null);
            Assert.Equal(new[] { weekStart.AddMinutes(-1), weekStart.AddMinutes(-2) }.Select(t => t.Ticks), older.Rows.Select(r => r.TimeTicks));
        }
    }
}
