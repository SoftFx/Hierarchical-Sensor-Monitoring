using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.Client.Session;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.HistoryRequests;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    /// <summary>
    /// Table sensors end to end against the real server (#1846 part 2): the managed collector registers a table
    /// sensor on its session, sends its rows as blocks on the table connection, at a tick and in the stop drain, and
    /// the rows are read back through <c>POST api/sensors/tableHistory</c>. Needs a server image built from a source
    /// that has table sensors (the fixture's image; see <see cref="HsmServerFixture"/>).
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class TableSensorEndToEndTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private readonly HsmServerFixture _fixture;

        public TableSensorEndToEndTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Rows_of_a_table_sensor_are_stored_by_the_server_and_read_back()
        {
            const int RowsAtTick = 300;
            const int RowsAtStop = 200;

            var path = $"tables/{Guid.NewGuid():N}/quotes";
            var options = _fixture.CreateCollectorOptions();

            options.TableTickPeriod = TimeSpan.FromSeconds(1);

            // Whole seconds, so the times read back compare exactly.
            var start = new DateTime(DateTime.UtcNow.AddMinutes(-10).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

            using (var collector = new DataCollector(options))
            {
                var table = collector.CreateTableSensor(path, new TableSensorOptions
                {
                    SensorLocation = SensorLocation.Product,
                    Description = "end-to-end table sensor",
                    Columns = { TableColumn.Double("bid"), TableColumn.Int64("size") },
                });

                await collector.Start();
                await UntilAsync(() => table.State != TableSensorState.Pending, () => "the table sensor was not registered");

                Assert.True(table.State == TableSensorState.Registered, $"the table sensor is {table.State}: {table.StateReason}");

                // The first rows go out with a tick, on the table connection.
                for (var row = 0; row < RowsAtTick; row++)
                    Assert.True(table.AddRow(start.AddMilliseconds(row), Bid(row), Size(row)));

                var tables = ((SessionDataSender)options.DataSender).Tables;
                var pipeline = collector.TablePipeline;

                await UntilAsync(() => tables.IsUp && pipeline.BufferedBytes == 0 && pipeline.BacklogBlocks == 0,
                                 () => $"the rows of the tick were not delivered (table connection: {tables.DownReason ?? "up"}, {pipeline.BacklogBlocks} block(s) waiting)");

                // The rest is still buffered when the collector stops: the stop drain packs and delivers it.
                for (var row = RowsAtTick; row < RowsAtTick + RowsAtStop; row++)
                    Assert.True(table.AddRow(start.AddMilliseconds(row), Bid(row), Size(row)));

                await collector.Stop();

                Assert.Equal(RowsAtTick + RowsAtStop, table.RowsAccepted);
                Assert.Equal(0, table.RowsDropped);
            }

            using (var client = _fixture.CreateSensorApiClient())
            {
                var request = new TableHistoryRequest
                {
                    Path = path,
                    From = start,
                    To = start.AddHours(1),
                    Format = TableHistoryFormat.Json,
                };

                using (var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"))
                using (var response = await client.PostAsync("/api/sensors/tableHistory", content))
                {
                    var body = await response.Content.ReadAsStringAsync();

                    Assert.True(response.StatusCode == HttpStatusCode.OK, $"tableHistory answered {(int)response.StatusCode}: {body}");
                    Assert.Equal("0", response.Headers.GetValues("X-Hsm-Blocks-Failed").Single());
                    Assert.Equal("false", response.Headers.GetValues("X-Hsm-Truncated").Single());

                    using (var document = JsonDocument.Parse(body))
                    {
                        var root = document.RootElement;

                        Assert.Equal(new[] { "time", "bid", "size" }, root.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
                        Assert.Equal(RowsAtTick + RowsAtStop, root.GetProperty("rowCount").GetInt32());

                        var rows = root.GetProperty("rows").EnumerateArray().ToList();

                        Assert.Equal(RowsAtTick + RowsAtStop, rows.Count);

                        for (var row = 0; row < rows.Count; row++)
                        {
                            var cells = rows[row].EnumerateArray().ToList();
                            var time = DateTime.Parse(cells[0].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

                            Assert.Equal(start.AddMilliseconds(row), time);

                            if (Bid(row).IsNull)
                                Assert.Equal(JsonValueKind.Null, cells[1].ValueKind);
                            else
                                Assert.Equal(BidValue(row), cells[1].GetDouble());

                            Assert.Equal(row, cells[2].GetInt64());
                        }
                    }
                }
            }
        }

        // Every seventh row has no bid: a null cell travels in the column's null map.
        private static TableCell Bid(int row) => row % 7 == 3 ? TableCell.Null : BidValue(row);

        private static double BidValue(int row) => 1.0841 + row / 100000.0;

        private static TableCell Size(int row) => (long)row;

        private static async Task UntilAsync(Func<bool> condition, Func<string> what)
        {
            var clock = Stopwatch.StartNew();

            while (!condition())
            {
                Assert.True(clock.Elapsed < Patience, what());
                await Task.Delay(50);
            }
        }
    }
}
