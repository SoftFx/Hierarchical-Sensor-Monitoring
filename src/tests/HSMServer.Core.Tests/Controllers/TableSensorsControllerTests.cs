using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBlocks;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Model.Authentication;
using HSMServer.SensorApi;
using HSMServer.ServerConfiguration;
using HSMServer.TablePages;
using HSMTableBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    public sealed class TableSensorsControllerFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableSensorsControllerTests);
    }


    /// <summary>
    /// The table pages' routes of the web UI (#1847): <c>POST /TableSensors/Chart</c> and <c>GET /TableSensors/Download</c>
    /// over a real tree and block store. Access for each action (a user of the product, of another product, an admin, a
    /// sensor that is not a table sensor, an unknown id, a store fault); the JSON of the chart; the bounds of a request;
    /// the shared read gate freed by a client that goes away; the CSV paging, its limits and the abort past the first byte.
    /// No timing: cancellation is driven through the read's seams.
    /// </summary>
    [Collection("Database collection")]
    public class TableSensorsControllerTests : MonitoringCoreTestsBase<TableSensorsControllerFixture>
    {
        private static readonly DateTime Hour0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime From = Hour0, To = Hour0.AddHours(1);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly Guid _producer = Guid.NewGuid();
        private readonly string _root = $"w{Guid.NewGuid():N}"[..12];
        private readonly Mock<IUserManager> _users = new();
        private long _producerSequence;
        private DateTime _now = Hour0.AddMinutes(30);

        private readonly TableBlockService _service;
        private TableReadGate _gate = new(1);
        private TableSensorsConfig _config = new() { MinFreeDiskMb = 0 };


        public TableSensorsControllerTests(TableSensorsControllerFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            var database = _databaseCoreManager.DatabaseCore;
            _service = new TableBlockService(database, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            ((TableBlockDatabase)database.TableBlocks).Clock = () => _now;
        }


        // === Access, for each action ===

        public static IEnumerable<object[]> Actions => [["chart"], ["download"]];

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_viewer_of_the_product_reads_the_sensor(string action)
        {
            var table = await SeedAsync(3);

            var (status, _) = await CallAsync(action, Viewer(_product), table.Id.ToString());

            Assert.Equal(200, status);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task An_admin_reads_any_sensor(string action)
        {
            var table = await SeedAsync(3);

            var (status, _) = await CallAsync(action, new User("admin") { IsAdmin = true }, table.Id.ToString());

            Assert.Equal(200, status);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_user_of_another_product_gets_exactly_the_answer_of_an_unknown_id(string action)
        {
            var table = await SeedAsync(3);
            var outsider = Viewer(Guid.NewGuid());

            var hidden = await CallAsync(action, outsider, table.Id.ToString());
            var unknown = await CallAsync(action, outsider, Guid.NewGuid().ToString());
            var malformed = await CallAsync(action, outsider, "not-an-id");

            Assert.Equal((404, TablePageService.NotFoundText), hidden);
            Assert.Equal(hidden, unknown);
            Assert.Equal(hidden, malformed);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_sensor_that_is_not_a_table_sensor_is_a_400(string action)
        {
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, $"{_root}/plain")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            });
            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, $"{_root}/plain", out var plain));

            var (status, text) = await CallAsync(action, Viewer(_product), plain.Id.ToString());

            Assert.Equal(400, status);
            Assert.Contains("is not a table sensor", text);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_store_fault_is_a_503_with_Retry_After(string action)
        {
            var table = await SeedAsync(3);
            var fault = new TableBlockStoreException("closed");
            var blocks = new StubBlocks(_service) { OnRead = (_, _) => throw fault };
            var charts = new Mock<ITableChartReader>();
            charts.Setup(c => c.Reduce(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(fault);

            var (status, text, context) = await CallFullAsync(action, Viewer(_product), table.Id.ToString(), Pages(blocks, charts.Object));

            Assert.Equal((503, TablePageService.StoreFaultResult.Text), (status, text));
            Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
            Assert.Equal(1, _gate.FreeTurns);
        }

        [Fact]
        public async Task A_store_that_changed_under_every_plan_of_a_chart_is_a_503_to_try_again()
        {
            var table = await SeedAsync(3);
            var charts = new Mock<ITableChartReader>();
            charts.Setup(c => c.Reduce(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new TableReadChangedException("changed"));

            var (status, text, context) = await CallFullAsync("chart", Viewer(_product), table.Id.ToString(), Pages(charts: charts.Object));

            Assert.Equal((503, TablePageService.StoreChangedResult.Text), (status, text));
            Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
            Assert.Equal(1, _gate.FreeTurns);
        }

        // === The chart's answer ===

        [Fact]
        public async Task The_chart_answers_buckets_with_UTC_times_null_for_a_bucket_without_a_value_and_the_counts()
        {
            var a = await RegisterAsync("a");
            // 300 rows in the first half of the hour, one null bid, one NaN; none in the second half.
            var rows = Enumerable.Range(0, 300).Select(i => (Hour0.AddSeconds(i * 6).Ticks, i == 7 ? null : i == 8 ? double.NaN : (double?)i, (long?)i)).ToArray();
            Put(("a", rows));

            var json = await ChartJsonAsync(a, Viewer(_product), new TableChartRequest { From = From, To = To, Columns = [1, 2], Buckets = 100 });

            Assert.Equal("buckets", json.GetProperty("mode").GetString());
            Assert.Equal(100, json.GetProperty("bucketCount").GetInt32());
            Assert.Equal("2026-11-24T09:00:00Z", json.GetProperty("from").GetString());
            Assert.Equal("2026-11-24T09:00:36Z", json.GetProperty("times")[1].GetString());
            Assert.Equal(300, json.GetProperty("rowCount").GetInt64());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("completeTo").ValueKind);
            Assert.Equal((0, 0), (json.GetProperty("blocksFailed").GetInt32(), json.GetProperty("batchesFailed").GetInt32()));

            var bid = json.GetProperty("columns")[0];
            Assert.Equal(("bid", "double", false), (bid.GetProperty("name").GetString(), bid.GetProperty("type").GetString(), bid.GetProperty("approximate").GetBoolean()));
            Assert.Equal((1, 1), (bid.GetProperty("nullCount").GetInt64(), bid.GetProperty("nonFinite").GetInt64()));
            Assert.Equal(0, bid.GetProperty("min")[0].GetDouble());
            Assert.Equal(5, bid.GetProperty("max")[0].GetDouble());
            Assert.Equal(JsonValueKind.Null, bid.GetProperty("min")[99].ValueKind);
            Assert.Equal(JsonValueKind.Null, bid.GetProperty("values").ValueKind);
        }

        [Fact]
        public async Task An_int64_beyond_2_pow_53_is_a_string_and_its_column_is_marked_approximate()
        {
            var a = await RegisterAsync("a");
            const long exact = 1L << 53, beyond = (1L << 53) + 1;
            Put(("a", Enumerable.Range(0, 300).Select(i => (Hour0.AddSeconds(i).Ticks, (double?)1.0, (long?)(i == 0 ? exact : i == 299 ? beyond : -beyond))).ToArray()));

            var json = await ChartJsonAsync(a, Viewer(_product), new TableChartRequest { From = From, To = To, Columns = [2], Buckets = 100 });
            var size = json.GetProperty("columns")[0];

            Assert.True(size.GetProperty("approximate").GetBoolean());
            Assert.Equal(JsonValueKind.String, size.GetProperty("min")[0].ValueKind);
            Assert.Equal((-beyond).ToString(), size.GetProperty("min")[0].GetString());
            Assert.Equal(exact, size.GetProperty("max")[0].GetInt64());
        }

        [Fact]
        public async Task A_short_range_is_answered_with_its_rows_a_NaN_as_null()
        {
            var a = await RegisterAsync("a");
            Put(("a", [(Hour0.AddMinutes(2).Ticks, 2.5, 20), (Hour0.AddMinutes(1).Ticks, double.NaN, null)]));

            var json = await ChartJsonAsync(a, Viewer(_product), new TableChartRequest { From = From, To = To, Buckets = 100 });

            Assert.Equal("rows", json.GetProperty("mode").GetString());
            Assert.Equal(new[] { "2026-11-24T09:01:00Z", "2026-11-24T09:02:00Z" }, json.GetProperty("times").EnumerateArray().Select(t => t.GetString()));

            // Columns not named: every double column.
            var bid = Assert.Single(json.GetProperty("columns").EnumerateArray());
            Assert.Equal("bid", bid.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, bid.GetProperty("values")[0].ValueKind);
            Assert.Equal(2.5, bid.GetProperty("values")[1].GetDouble());
            Assert.Equal(1, bid.GetProperty("nonFinite").GetInt64());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("bucketRows").ValueKind);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(5, 100)]
        [InlineData(640, 640)]
        [InlineData(100_000, 2000)]
        public async Task The_bucket_count_is_clamped_to_100_to_2000(int asked, int expected)
        {
            var a = await RegisterAsync("a");

            var json = await ChartJsonAsync(a, Viewer(_product), new TableChartRequest { From = From, To = To, Columns = [], Buckets = asked });

            Assert.Equal(expected, json.GetProperty("bucketCount").GetInt32());
        }

        [Fact]
        public async Task A_request_the_reducer_refuses_is_a_400_with_its_reason()
        {
            var a = await RegisterAsync("a");

            var (status, text) = await CallAsync("chart", Viewer(_product), a.Id.ToString(), request: new TableChartRequest { Id = a.Id.ToString(), From = From, To = To, Columns = [5], Buckets = 100 });
            var (rangeStatus, _) = await CallAsync("chart", Viewer(_product), a.Id.ToString(), request: new TableChartRequest { Id = a.Id.ToString(), From = To, To = From, Buckets = 100 });

            Assert.Equal(400, status);
            Assert.Contains("is not a value column", text);
            Assert.Equal(400, rangeStatus);
        }

        // === The shared read gate ===

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_client_that_goes_away_during_the_read_frees_its_turn(string action)
        {
            var table = await SeedAsync(3);
            Put(("a", [(Hour0.AddMinutes(10).Ticks, 1.0, 1)]));

            // The first block cancels the request: the read stops before the second.
            using var cancel = new CancellationTokenSource();
            var blocksRead = 0;
            _service.BeforeBlockRead = _ =>
            {
                blocksRead++;
                cancel.Cancel();
            };

            var (status, body, _) = await CallFullAsync(action, Viewer(_product), table.Id.ToString(), token: cancel.Token);

            Assert.Equal((200, ""), (status, body)); // An empty answer: nobody reads it.
            Assert.Equal(1, blocksRead);
            Assert.Equal(1, _gate.FreeTurns);

            _service.BeforeBlockRead = null;
            Assert.Equal(200, (await CallAsync(action, Viewer(_product), table.Id.ToString())).Status);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task A_client_that_goes_away_while_waiting_for_a_turn_takes_none(string action)
        {
            var table = await SeedAsync(3);
            await _gate.WaitAsync(CancellationToken.None);

            using var cancel = new CancellationTokenSource();
            var call = CallFullAsync(action, Viewer(_product), table.Id.ToString(), token: cancel.Token);
            Assert.False(call.IsCompleted);

            cancel.Cancel();
            await call;
            Assert.Equal(0, _gate.FreeTurns);

            _gate.Release();
            Assert.Equal(1, _gate.FreeTurns);
        }

        // === The download ===

        [Fact]
        public async Task A_download_pages_past_the_text_limit_and_is_the_read_routes_CSV_with_one_header()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5 };
            var table = await SeedAsync(23);

            var (status, csv, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString());

            Assert.Equal(200, status);
            Assert.Equal(await ReferenceCsvAsync(table), csv);
            Assert.Equal(24, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.False(Aborted(context));
            Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString());
        }

        [Fact]
        public async Task A_time_with_more_rows_than_a_page_is_read_again_whole()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5 };
            var a = await RegisterAsync("a");
            var at = Hour0.AddMinutes(3).Ticks;
            Put(("a", [(Hour0.AddMinutes(1).Ticks, 1.0, 1), .. Enumerable.Range(0, 8).Select(i => (at, (double?)i, (long?)i)), (Hour0.AddMinutes(4).Ticks, 9.0, 9)]));

            var (status, csv, context) = await CallFullAsync("download", Viewer(_product), a.Id.ToString());

            Assert.Equal(200, status);
            Assert.Equal(await ReferenceCsvAsync(a), csv);
            Assert.Equal(11, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.False(Aborted(context));
        }

        [Fact]
        public async Task More_rows_than_the_download_limit_in_the_first_page_are_refused_before_the_first_byte()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5, MaxDownloadRows = 3 };
            var table = await SeedAsync(10);

            var (status, text, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString());

            Assert.Equal(400, status);
            Assert.Contains("MaxDownloadRows", text);
            Assert.False(Aborted(context));
        }

        [Fact]
        public async Task More_rows_than_the_download_limit_found_while_writing_abort_the_connection()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5, MaxDownloadRows = 7 };
            var table = await SeedAsync(10);

            var (status, csv, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString());

            Assert.Equal(200, status);
            Assert.True(Aborted(context));
            Assert.Equal(6, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // the header and the first page
        }

        [Fact]
        public async Task A_download_of_exactly_the_limit_is_complete()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5, MaxDownloadRows = 10 };
            var table = await SeedAsync(10);

            var (status, csv, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString());

            Assert.Equal(200, status);
            Assert.False(Aborted(context));
            Assert.Equal(11, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Fact]
        public async Task Failed_blocks_known_before_the_first_byte_are_an_error_the_page_can_show()
        {
            var table = await SeedAsync(3);
            var blocks = new StubBlocks(_service) { OnRead = (rows, _) => rows with { BlocksFailed = 2 } };

            var (status, text, _) = await CallFullAsync("download", Viewer(_product), table.Id.ToString(), Pages(blocks));

            Assert.Equal(500, status);
            Assert.Contains("2 stored block(s)", text);
        }

        [Fact]
        public async Task Failed_batches_found_after_the_first_byte_abort_the_connection()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5 };
            var table = await SeedAsync(12);
            var blocks = new StubBlocks(_service) { OnRead = (rows, call) => call == 2 ? rows with { BatchesFailed = 1 } : rows };

            var (status, _, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString(), Pages(blocks));

            Assert.Equal(200, status);
            Assert.True(Aborted(context));
        }

        [Fact]
        public async Task A_store_that_changed_under_every_plan_of_the_first_page_is_a_503_to_try_again()
        {
            var table = await SeedAsync(3);
            var blocks = new StubBlocks(_service) { OnRead = (_, _) => throw new TableReadChangedException("changed") };

            var (status, text, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString(), Pages(blocks));

            Assert.Equal((503, TablePageService.StoreChangedResult.Text), (status, text));
            Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
            Assert.False(Aborted(context));
            Assert.Equal(1, _gate.FreeTurns);
        }

        [Fact]
        public async Task A_store_that_changed_under_every_plan_of_a_later_page_aborts_the_connection()
        {
            _config = new TableSensorsConfig { MinFreeDiskMb = 0, MaxTextReadRows = 5 };
            var table = await SeedAsync(12);
            var blocks = new StubBlocks(_service) { OnRead = (rows, call) => call == 2 ? throw new TableReadChangedException("changed") : rows };

            var (status, csv, context) = await CallFullAsync("download", Viewer(_product), table.Id.ToString(), Pages(blocks));

            Assert.Equal(200, status);
            Assert.True(Aborted(context));
            Assert.Equal(6, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal(1, _gate.FreeTurns);
        }

        [Fact]
        public void The_file_name_is_the_history_exports_with_unsafe_characters_replaced()
        {
            var name = TablePageService.FileName("prod/quotes/EUR:USD*", Hour0, Hour0.AddHours(1));

            Assert.Equal("prod_quotes_EUR_USD__from_2026-11-24T09_00_00_to2026-11-24T10_00_00.csv", name);
        }

        [Fact]
        public void The_download_limit_defaults_to_ten_million_rows_and_is_validated()
        {
            Assert.Equal(10_000_000, new TableSensorsConfig().MaxDownloadRows);

            var error = Assert.Throws<InvalidOperationException>(() => new TableSensorsConfig { MaxDownloadRows = 0 }.Validate());
            Assert.Contains("TableSensors.MaxDownloadRows = 0", error.Message);
        }


        // === Helpers ===

        // What the results need to execute (their executors, JSON options, logging).
        private static readonly IServiceProvider MvcServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging().AddControllers().Services.BuildServiceProvider();

        private static User Viewer(Guid product) => new("viewer") { ProductsRoles = { (product, ProductRoleEnum.ProductViewer) } };

        private TablePageService Pages(ITableBlockService blocks = null, ITableChartReader charts = null)
        {
            var config = new Mock<IServerConfig>();
            config.Setup(c => c.TableSensors).Returns(_config);

            return new TablePageService(_valuesCache, blocks ?? _service, charts ?? _service, _gate, config.Object);
        }

        private async Task<(int Status, string Text)> CallAsync(string action, User user, string id, TablePageService pages = null, TableChartRequest request = null)
        {
            var (status, text, _) = await CallFullAsync(action, user, id, pages, request: request);
            return (status, text);
        }

        private async Task<(int Status, string Text, HttpContext Context)> CallFullAsync(string action, User user, string id, TablePageService pages = null,
            CancellationToken token = default, TableChartRequest request = null)
        {
            var lifetime = new Lifetime(token);
            var context = new DefaultHttpContext { User = user };
            context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
            context.RequestServices = MvcServices;
            context.Response.Body = new MemoryStream();

            var controller = new TableSensorsController(_users.Object, pages ?? Pages())
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };

            var result = action == "chart"
                ? await controller.Chart(request ?? new TableChartRequest { Id = id, From = From, To = To, Buckets = 100 })
                : await controller.Download(id, From, To);

            await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
            await context.Response.BodyWriter.FlushAsync();

            return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()), context);
        }

        private async Task<JsonElement> ChartJsonAsync(BaseSensorModel sensor, User user, TableChartRequest request)
        {
            request.Id = sensor.Id.ToString();
            var (status, text) = await CallAsync("chart", user, sensor.Id.ToString(), request: request);
            Assert.True(status == 200, text);
            return JsonDocument.Parse(text).RootElement;
        }

        private static bool Aborted(HttpContext context) => ((Lifetime)context.Features.Get<IHttpRequestLifetimeFeature>()).WasAborted;

        // The read route's CSV of the whole range in one answer.
        private async Task<string> ReferenceCsvAsync(BaseSensorModel sensor)
        {
            var rows = _service.Read(sensor.Id, From, To, 1_000_000).Value;
            var stream = new MemoryStream();
            var writer = System.IO.Pipelines.PipeWriter.Create(stream);
            await TableRowsText.WriteCsvAsync(writer, rows, CancellationToken.None);
            await writer.CompleteAsync();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        // A table sensor with rows one second apart in the range, the bid and size from their index.
        private async Task<BaseSensorModel> SeedAsync(int rows)
        {
            var a = await RegisterAsync("a");
            Put(("a", Enumerable.Range(0, rows).Select(i => (Hour0.AddSeconds(i).Ticks, (double?)(i * 0.5), (long?)i)).ToArray()));
            return a;
        }

        private async Task<BaseSensorModel> RegisterAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            return sensor;
        }

        private void Put(params (string Name, (long Time, double? Bid, long? Size)[] Rows)[] tables)
        {
            using var writer = new TableBlockWriter();

            foreach (var (name, rows) in tables)
            {
                var bidNulls = new byte[TableNullMap.ByteLength(rows.Length)];
                var sizeNulls = new byte[TableNullMap.ByteLength(rows.Length)];
                bool anyBid = false, anySize = false;

                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].Bid is null) { TableNullMap.SetNull(bidNulls, i); anyBid = true; }
                    if (rows[i].Size is null) { TableNullMap.SetNull(sizeNulls, i); anySize = true; }
                }

                writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
                writer.AddTimeColumn(rows.Select(r => r.Time).ToArray());
                writer.AddDoubleColumn(rows.Select(r => r.Bid ?? 0).ToArray(), anyBid ? bidNulls : null);
                writer.AddInt64Column(rows.Select(r => r.Size ?? 0).ToArray(), anySize ? sizeNulls : null);
            }

            var put = _service.Put(_product, _producer, ++_producerSequence, writer.ToArray());
            Assert.Equal(TableBlockPutOutcome.Stored, put.Outcome);
        }


        private sealed class Lifetime(CancellationToken token) : IHttpRequestLifetimeFeature
        {
            public CancellationToken RequestAborted { get; set; } = token;

            public bool WasAborted { get; private set; }

            public void Abort() => WasAborted = true;
        }

        // The real block service, with its reads seen (and changed) by the test; the n-th read is numbered from 1.
        private sealed class StubBlocks(ITableBlockService inner) : ITableBlockService
        {
            private int _reads;

            public Func<TableRows, int, TableRows> OnRead { get; init; }

            public TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block) =>
                inner.Put(productId, producerId, producerSequence, block);

            public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null, CancellationToken token = default)
            {
                var result = inner.Read(sensorId, from, to, maxRows, token);
                return result.IsOk ? TaskResult<TableRows>.FromValue(OnRead(result.Value, Interlocked.Increment(ref _reads))) : result;
            }
        }
    }
}
