using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.History;
using HSMServer.Model.Model.History;
using Moq;
using Xunit;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Model
{
    // #1509: the sensor history table shows a σ column only when a loaded bar reports StdDev;
    // unknown σ is an empty cell, never "0". Pages are fed through the real Reload/ToNextPage
    // path from a mocked cache that serves the given pages in order (newest first).
    public class HistoryTableViewModelStdDevTests
    {
        private static readonly DateTime Start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);


        [Fact]
        [Trait("Category", "Simple")]
        public async Task Int_bar_with_stddev_shows_the_column_and_the_formatted_value()
        {
            using var table = await Load(SensorType.IntegerBar, [[IntBar(0, 1.5)]]);

            Assert.True(table.HasStdDev);
            Assert.Equal(1.5.ToString(), Assert.Single(Rows(table)).StdDev);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public async Task Double_bar_with_stddev_shows_the_column_and_the_formatted_value()
        {
            using var table = await Load(SensorType.DoubleBar, [[DoubleBar(0, 2.25)]]);

            Assert.True(table.HasStdDev);
            Assert.Equal(2.25.ToString(), Assert.Single(Rows(table)).StdDev);
        }

        [Theory]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [Trait("Category", "Simple")]
        public async Task Bars_without_stddev_hide_the_column_and_never_show_zero(SensorType type)
        {
            using var table = await Load(type, [[Bar(type, 0, null), Bar(type, 1, null)]]);

            Assert.False(table.HasStdDev);
            Assert.All(Rows(table), row => Assert.Null(row.StdDev));
        }

        [Theory]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [Trait("Category", "Simple")]
        public async Task A_timeout_row_alone_does_not_show_the_column(SensorType type)
        {
            // A timeout row repeats the last bar (σ included) but is rendered empty.
            var timeout = Bar(type, 1, 1.5) with { IsTimeout = true };

            using var table = await Load(type, [[Bar(type, 0, null), timeout]]);

            Assert.False(table.HasStdDev);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public async Task Column_does_not_disappear_when_paging_to_pages_without_stddev()
        {
            using var table = await Load(SensorType.DoubleBar,
                [[DoubleBar(3, 1.5)], [DoubleBar(2, null)], [DoubleBar(1, null)], [DoubleBar(0, null)]]);

            Assert.True(table.HasStdDev);

            await table.ToNextPage();
            await table.ToNextPage();

            Assert.Equal(2, table.CurrentIndex);
            Assert.All(Rows(table), row => Assert.Null(row.StdDev));
            Assert.True(table.HasStdDev);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public async Task Column_appears_once_an_older_page_with_stddev_is_loaded()
        {
            // Reload reads the first two pages; the third is read by the first ToNextPage.
            using var table = await Load(SensorType.IntegerBar,
                [[IntBar(2, null)], [IntBar(1, null)], [IntBar(0, 0.5)]]);

            Assert.False(table.HasStdDev);

            await table.ToNextPage();

            Assert.All(Rows(table), row => Assert.Null(row.StdDev));
            Assert.True(table.HasStdDev);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public async Task Non_bar_sensor_never_shows_the_column()
        {
            using var table = await Load(SensorType.Double,
                [[new DoubleValue { Value = 1.5, Time = Start, ReceivingTime = Start }]]);

            Assert.False(table.IsBarSensor);
            Assert.False(table.HasStdDev);
        }


        private static async Task<HistoryTableViewModel> Load(SensorType type, List<BaseValue>[] pages)
        {
            var model = TestSensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)type));

            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.GetSensorValuesPage(model.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<RequestOptions>()))
                 .Returns(() => Enumerate(pages));

            var table = new HistoryTableViewModel(model);
            await table.Reload(cache.Object, new GetSensorHistoryRequest());

            return table;
        }

        private static async IAsyncEnumerable<List<BaseValue>> Enumerate(List<BaseValue>[] pages)
        {
            foreach (var page in pages)
            {
                await Task.Yield();
                yield return page;
            }
        }

        private static List<BarSensorValueViewModel> Rows(HistoryTableViewModel table) =>
            table.CurrentTablePage.Cast<BarSensorValueViewModel>().ToList();

        private static BaseValue Bar(SensorType type, int minute, double? stdDev) =>
            type == SensorType.IntegerBar ? IntBar(minute, stdDev) : DoubleBar(minute, stdDev);

        private static IntegerBarValue IntBar(int minute, double? stdDev) =>
            new()
            {
                Count = 4,
                Min = 1,
                Max = 9,
                Mean = 5,
                StdDev = stdDev,
                OpenTime = Start.AddMinutes(minute),
                CloseTime = Start.AddMinutes(minute + 1),
                Time = Start.AddMinutes(minute + 1),
                ReceivingTime = Start.AddMinutes(minute + 1),
            };

        private static DoubleBarValue DoubleBar(int minute, double? stdDev) =>
            new()
            {
                Count = 4,
                Min = 1,
                Max = 9,
                Mean = 5,
                StdDev = stdDev,
                OpenTime = Start.AddMinutes(minute),
                CloseTime = Start.AddMinutes(minute + 1),
                Time = Start.AddMinutes(minute + 1),
                ReceivingTime = Start.AddMinutes(minute + 1),
            };
    }
}
