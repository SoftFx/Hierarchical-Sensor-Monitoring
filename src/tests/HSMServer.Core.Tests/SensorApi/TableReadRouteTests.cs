using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class TableReadRouteFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableReadRouteTests);
    }


    // The read route of table sensors (#1845): POST api/sensors/tableHistory on a real Kestrel listener
    // over the real cache, block service and block store. Rows are put into the store directly; the
    // route is read with a plain HttpClient, as a script without the client library would.
    //
    // The texts of the answers are written out on purpose: they are the contract.
    public class TableReadRouteTests : MonitoringCoreTestsBase<TableReadRouteFixture>
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
        private static readonly DateTime T0 = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _root = $"r{Guid.NewGuid():N}"[..12];
        private readonly Guid _producer = Guid.NewGuid();
        private readonly TableBlockService _blocks;
        private readonly ReadControlledTableBlocks _tables;
        private long _sequence;


        public TableReadRouteTests(TableReadRouteFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _blocks = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            _tables = new ReadControlledTableBlocks(_blocks);
        }


        // === The three formats ===

        [Fact]
        public async Task Json_CarriesEveryValue_NullsAsNull_AndNonFiniteDoublesAsStrings()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            Put(Edges("q"));

            var answer = await ReadAsync(host, "q", T0, T0.AddMinutes(1));

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("application/json; charset=utf-8", answer.ContentType);
            Assert.Equal("6", answer.Header("X-Hsm-Row-Count"));
            Assert.Equal("false", answer.Header("X-Hsm-Truncated"));
            Assert.Equal("0", answer.Header("X-Hsm-Blocks-Failed"));
            Assert.Equal("0", answer.Header("X-Hsm-Batches-Failed"));
            Assert.Null(answer.Header("X-Hsm-Next-From"));

            Assert.Equal(
                $"{{\"path\":\"{_root}/q\",\"columns\":[{{\"name\":\"time\",\"type\":\"time\"}},{{\"name\":\"bid\",\"type\":\"double\"}},{{\"name\":\"size\",\"type\":\"int64\"}}]," +
                "\"rowCount\":6,\"truncated\":false,\"nextFrom\":null,\"rowsAtTimeOmitted\":false,\"blocksFailed\":0,\"batchesFailed\":0,\"rows\":[" +
                "[\"2026-11-25T09:00:00.0000000Z\",1.5,-9223372036854775808]," +
                "[\"2026-11-25T09:00:01.0000000Z\",null,2]," +
                "[\"2026-11-25T09:00:02.0000000Z\",\"NaN\",null]," +
                "[\"2026-11-25T09:00:03.0000000Z\",\"Infinity\",4]," +
                "[\"2026-11-25T09:00:04.0000000Z\",\"-Infinity\",9223372036854775807]," +
                "[\"2026-11-25T09:00:05.0000001Z\",-0,6]]}",
                Encoding.UTF8.GetString(answer.Body));
        }

        [Fact]
        public async Task Csv_IsRfc4180_WithAnEmptyCellForNull()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            Put(Edges("q"));

            var answer = await ReadAsync(host, "q", T0, T0.AddMinutes(1), format: 1);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("text/csv; charset=utf-8", answer.ContentType);
            Assert.Equal(
                "time,bid,size\r\n" +
                "2026-11-25T09:00:00.0000000Z,1.5,-9223372036854775808\r\n" +
                "2026-11-25T09:00:01.0000000Z,,2\r\n" +
                "2026-11-25T09:00:02.0000000Z,NaN,\r\n" +
                "2026-11-25T09:00:03.0000000Z,Infinity,4\r\n" +
                "2026-11-25T09:00:04.0000000Z,-Infinity,9223372036854775807\r\n" +
                "2026-11-25T09:00:05.0000001Z,-0,6\r\n",
                Encoding.UTF8.GetString(answer.Body));
        }

        [Fact]
        public async Task Csv_QuotesAColumnNameThatNeedsIt()
        {
            await using var host = await StartHost();
            await RegisterAsync("named", new TableSchema([
                new TableSchemaColumn("time", TableColumnType.Time),
                new TableSchemaColumn("bid,best", TableColumnType.Double),
                new TableSchemaColumn("say \"hi\"", TableColumnType.Int64),
            ]));

            var answer = await ReadAsync(host, "named", T0, T0.AddMinutes(1), format: 1);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("time,\"bid,best\",\"say \"\"hi\"\"\"\r\n", Encoding.UTF8.GetString(answer.Body));
        }

        // The answer for the client library: blocks of the one table, more than one when the rows are
        // more than a block holds, with the null maps cut to each block. Read back by the block reader,
        // they are the JSON answer value for value.
        [Fact]
        public async Task Blocks_ReadBackByTheBlockReader_AreTheJsonAnswerValueForValue()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");

            const int rows = 70_000;
            var bids = Enumerable.Range(0, rows).Select(i => i % 997 == 0 ? (double?)null : i * 0.5).ToArray();
            var sizes = Enumerable.Range(0, rows).Select(i => i == 65_537 ? (long?)null : i).ToArray();

            Put(Table("q", Enumerable.Range(0, rows).Select(i => (T0.AddMilliseconds(i), bids[i], sizes[i])).ToArray()));

            var blocks = await ReadAsync(host, "q", T0, T0.AddHours(1), format: 2);
            var json = await ReadAsync(host, "q", T0, T0.AddHours(1));

            Assert.Equal(HttpStatusCode.OK, blocks.Status);
            Assert.Equal("application/vnd.hsm.table-blocks", blocks.ContentType);
            Assert.Equal("time;1,bid;3,size;2", blocks.Header("X-Hsm-Table-Columns"));
            Assert.Equal(rows.ToString(), blocks.Header("X-Hsm-Row-Count"));

            var read = ReadBlocks(blocks.Body);

            Assert.Equal(2, read.Count);
            Assert.All(read, table => Assert.Equal($"{_root}/q", Encoding.UTF8.GetString(table.TableId)));
            Assert.All(read, table => Assert.Equal(Quotes.Id, table.SchemaId));

            var jsonRows = JsonNode.Parse(json.Body)["rows"].AsArray();
            var row = 0;

            foreach (var table in read)
            {
                for (var i = 0; i < table.RowCount; i++, row++)
                {
                    var expected = jsonRows[row].AsArray();

                    Assert.Equal((string)expected[0], new DateTime(table.Columns[0].Int64Values[i], DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"));
                    Assert.Equal(expected[1] is null, table.Columns[1].IsNull(i));
                    Assert.Equal(expected[2] is null, table.Columns[2].IsNull(i));

                    if (expected[1] is not null)
                        Assert.Equal((double)expected[1], table.Columns[1].DoubleValues[i]);

                    if (expected[2] is not null)
                        Assert.Equal((long)expected[2], table.Columns[2].Int64Values[i]);
                }
            }

            Assert.Equal(rows, row);
            Assert.True(read[1].Columns[2].IsNull(65_537 - 65_536));
            Assert.Equal(rows, Read(sensor).RowCount);
        }

        [Fact]
        public async Task AnEmptyRange_Is200_WithNoRows_InEveryFormat()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            Put(Edges("q"));

            var json = await ReadAsync(host, "q", T0.AddHours(1), T0.AddHours(2));
            var csv = await ReadAsync(host, "q", T0.AddHours(1), T0.AddHours(2), format: 1);
            var blocks = await ReadAsync(host, "q", T0.AddHours(1), T0.AddHours(2), format: 2);

            Assert.Equal(0, (int)JsonNode.Parse(json.Body)["rowCount"]);
            Assert.Equal("time,bid,size\r\n", Encoding.UTF8.GetString(csv.Body));
            Assert.Empty(blocks.Body);
            Assert.All(new[] { json, csv, blocks }, answer => Assert.Equal("0", answer.Header("X-Hsm-Row-Count")));
        }


        // A batch of a merged hour that cannot be decoded (#1850): its rows are missing, and every format
        // says so in X-Hsm-Batches-Failed (JSON in its body too), apart from the blocks' figure.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ADamagedBatch_IsReportedInEveryFormat_ApartFromTheBlocks(int format)
        {
            await using var host = await StartHost();
            var sensor = (TableSensorModel)await RegisterAsync("q");

            WriteDamagedBatch(sensor, T0, T0.AddSeconds(1));

            var answer = await ReadAsync(host, "q", T0, T0.AddMinutes(1), format: format);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("0", answer.Header("X-Hsm-Row-Count"));
            Assert.Equal("false", answer.Header("X-Hsm-Truncated"));
            Assert.Equal("0", answer.Header("X-Hsm-Blocks-Failed"));
            Assert.Equal("1", answer.Header("X-Hsm-Batches-Failed"));

            if (format == 0)
            {
                var body = JsonNode.Parse(answer.Body);

                Assert.Equal(0, (int)body["blocksFailed"]);
                Assert.Equal(1, (int)body["batchesFailed"]);
            }
        }


        // === The limit and paging ===

        // Every page boundary falls inside a run of equal times, in rows that arrived in different
        // blocks: paging by X-Hsm-Next-From returns every row exactly once, in order.
        [Fact]
        public async Task PagingByNextFrom_OverEqualTimesAtEveryBoundary_ReturnsEveryRowExactlyOnce()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            // Ten times, three rows each, the three of a time in three blocks.
            for (var block = 0; block < 3; block++)
                Put(Table("q", Enumerable.Range(0, 10).Select(t => (T0.AddSeconds(t), (double?)(t * 10 + block), (long?)block)).ToArray()));

            var expected = Enumerable.Range(0, 10).SelectMany(t => Enumerable.Range(0, 3).Select(block => (double)(t * 10 + block))).ToList();
            var got = new List<double>();
            var from = T0;
            var pages = 0;

            while (true)
            {
                var page = await ReadAsync(host, "q", from, T0.AddMinutes(1), maxRows: 4);
                var body = JsonNode.Parse(page.Body);

                got.AddRange(body["rows"].AsArray().Select(row => (double)row[1]));
                pages++;

                Assert.True(pages <= 20, "paging does not end");

                if (page.Header("X-Hsm-Truncated") == "false")
                {
                    Assert.Null(page.Header("X-Hsm-Next-From"));
                    break;
                }

                // A page holds whole times: 3 of the 4 allowed rows, never the first row of the next time alone.
                Assert.Equal(3, (int)body["rowCount"]);
                Assert.Equal((string)body["nextFrom"], page.Header("X-Hsm-Next-From"));

                from = DateTime.Parse(page.Header("X-Hsm-Next-From"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
            }

            Assert.Equal(expected, got);
            Assert.Equal(10, pages);
        }

        // One time with more rows than the limit: the rows are cut at the limit, the answer says so, and
        // the next page starts one tick later.
        [Fact]
        public async Task OneTimeOverTheLimit_IsCutAtTheLimit_AndTheNextPageStartsOneTickLater()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            Put(Table("q", Enumerable.Range(0, 10).Select(i => (T0, (double?)i, (long?)i)).Append((T0.AddSeconds(1), 99.0, 99L)).ToArray()));

            var page = await ReadAsync(host, "q", T0, T0.AddMinutes(1), maxRows: 4);
            var body = JsonNode.Parse(page.Body);

            Assert.Equal("4", page.Header("X-Hsm-Row-Count"));
            Assert.Equal("true", page.Header("X-Hsm-Truncated"));
            Assert.Equal("true", page.Header("X-Hsm-Rows-At-Time-Omitted"));
            Assert.Equal("2026-11-25T09:00:00.0000001Z", page.Header("X-Hsm-Next-From"));
            Assert.True((bool)body["rowsAtTimeOmitted"]);
            Assert.Equal(new[] { 0.0, 1, 2, 3 }, body["rows"].AsArray().Select(row => (double)row[1]));

            var next = JsonNode.Parse((await ReadAsync(host, "q", T0.AddTicks(1), T0.AddMinutes(1), maxRows: 4)).Body);

            Assert.Equal(new[] { 99.0 }, next["rows"].AsArray().Select(row => (double)row[1]));
        }

        // The same when that one time is the last tick of the range: the next page would start at To,
        // so there is none. The answer is cut and says so, and has no X-Hsm-Next-From: a client stops
        // there instead of asking for an empty range (a 400).
        [Fact]
        public async Task OneTimeOverTheLimit_AtTheLastTickOfTheRange_HasNoNextPage()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            Put(Table("q", Enumerable.Range(0, 10).Select(i => (T0, (double?)i, (long?)i)).ToArray()));

            var page = await ReadAsync(host, "q", T0, T0.AddTicks(1), maxRows: 4);
            var body = JsonNode.Parse(page.Body);

            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Equal("4", page.Header("X-Hsm-Row-Count"));
            Assert.Equal("true", page.Header("X-Hsm-Truncated"));
            Assert.Equal("true", page.Header("X-Hsm-Rows-At-Time-Omitted"));
            Assert.Null(page.Header("X-Hsm-Next-From"));
            Assert.Null((string)body["nextFrom"]);
        }

        // The text formats are capped by MaxTextReadRows, the blocks answer by MaxReadRows; a larger
        // MaxRows is capped, and the answer says it was cut.
        [Fact]
        public async Task TheRowCaps_OfTheTextFormats_AndOfTheBlocks()
        {
            await using var host = await StartHost(tables => { tables.MaxTextReadRows = 5; tables.MaxReadRows = 8; });
            await RegisterAsync("q");
            Put(Table("q", Enumerable.Range(0, 20).Select(i => (T0.AddSeconds(i), (double?)i, (long?)i)).ToArray()));

            var json = await ReadAsync(host, "q", T0, T0.AddMinutes(1), maxRows: 100);
            var csv = await ReadAsync(host, "q", T0, T0.AddMinutes(1), maxRows: 100, format: 1);
            var blocks = await ReadAsync(host, "q", T0, T0.AddMinutes(1), maxRows: 100, format: 2);
            var asked = await ReadAsync(host, "q", T0, T0.AddMinutes(1), maxRows: 3);

            Assert.Equal("5", json.Header("X-Hsm-Row-Count"));
            Assert.Equal("true", json.Header("X-Hsm-Truncated"));
            Assert.Equal("2026-11-25T09:00:05.0000000Z", json.Header("X-Hsm-Next-From"));
            Assert.Equal(6, Encoding.UTF8.GetString(csv.Body).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal("8", blocks.Header("X-Hsm-Row-Count"));
            Assert.Equal(8, ReadBlocks(blocks.Body).Sum(table => table.RowCount));
            Assert.Equal("3", asked.Header("X-Hsm-Row-Count"));
        }


        // === Refusals ===

        [Theory]
        [InlineData("{\"Path\":\"\",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\"}", "The request has no path.")]
        [InlineData("{\"Path\":\"PATH\",\"From\":\"2026-11-25T10:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\"}", "From must be before To: the range is [From, To).")]
        [InlineData("{\"Path\":\"PATH\",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\",\"Format\":7}", "The format 7 is unknown: 0 JSON, 1 CSV, 2 blocks.")]
        [InlineData("{\"Path\":\"PATH\",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\",\"MaxRows\":0}", "MaxRows must be positive.")]
        [InlineData("{\"Path\":\"PATH\",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\",\"Format\":2,\"MaxBlockFormatVersion\":0}", "This server writes table block format 1; the client reads up to 0.")]
        [InlineData("{\"Path\":\"INT\",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\"}", "is not a table sensor.")]
        public async Task ARequestThatCannotBeAnswered_Is400_WithTheReason(string body, string reason)
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            await CreateIntSensorAsync("int");

            var answer = await PostAsync(host, body.Replace("PATH", $"{_root}/q").Replace("INT", $"{_root}/int"), _key);

            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Contains(reason, Encoding.UTF8.GetString(answer.Body));
        }

        [Fact]
        public async Task ABodyThatCannotBeRead_Is400()
        {
            await using var host = await StartHost();

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(host, "{\"Path\":", _key)).Status);
        }

        [Fact]
        public async Task APathWithNoSensorInTheKeysProduct_Is404()
        {
            await using var host = await StartHost();

            var answer = await ReadAsync(host, "missing", T0, T0.AddHours(1));

            Assert.Equal(HttpStatusCode.NotFound, answer.Status);
            Assert.Equal($"No sensor \"{_root}/missing\" in the product of the key.", Encoding.UTF8.GetString(answer.Body));
        }

        // A path is found in its normal form, as it was registered.
        [Fact]
        public async Task APathInAnotherSpelling_FindsTheSensor()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            Put(Edges("q"));

            var answer = await PostAsync(host, $"{{\"Path\":\"/{_root}// q \",\"From\":\"2026-11-25T09:00:00Z\",\"To\":\"2026-11-25T10:00:00Z\"}}", _key);

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Equal("6", answer.Header("X-Hsm-Row-Count"));
        }

        [Theory]
        [InlineData("none")]
        [InlineData("send only")]
        [InlineData("blocked")]
        public async Task AKeyThatMayNotRead_Is406_AndNothingIsRead(string access)
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            Put(Edges("q"));

            var key = access switch
            {
                "none" => null,
                "send only" => AddKey(KeyPermissions.CanSendSensorData).ToString(),
                _ => Block(AddKey()).ToString(),
            };

            var answer = await PostAsync(host, Body("q", T0, T0.AddHours(1)), key);

            Assert.Equal(HttpStatusCode.NotAcceptable, answer.Status);
            Assert.Equal(0, _tables.Reads);
        }

        // A store that cannot be read now is the server's fault, not the request's.
        [Fact]
        public async Task AFaultWhileTheKeyIsLookedUp_Is503_WithRetryAfter_NotTheRefusalOfTheKey()
        {
            // The server fails while it looks the key up (#1756): a fault the same request later is
            // answered for, not the 406 of a key that may not read, which a client takes as final.
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig(), tables: _tables, tableSensors: new TableSensorsConfig());
            await RegisterAsync("q");

            faulting.FaultOnKeyLookup();

            var answer = await ReadAsync(host, "q", T0, T0.AddHours(1));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status);
            Assert.Equal("60", answer.Header("Retry-After"));

            faulting.Recover();

            Assert.Equal(HttpStatusCode.OK, (await ReadAsync(host, "q", T0, T0.AddHours(1))).Status);
        }

        [Fact]
        public async Task AStoreFault_Is503_WithRetryAfter()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            _tables.ReadFault = true;

            var answer = await ReadAsync(host, "q", T0, T0.AddHours(1));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status);
            Assert.Equal("60", answer.Header("Retry-After"));
            Assert.Equal("The table block store cannot be read now.", Encoding.UTF8.GetString(answer.Body));
        }


        // === Cancellation ===

        // The client goes away while the read is inside the store: the read stops before the next block.
        [Fact]
        public async Task ACancelledRequest_StopsTheReadBetweenBlocks()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            for (var block = 0; block < 3; block++)
                Put(Table("q", (T0.AddSeconds(block), 1.0, 1L)));

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocksRead = 0;

            _blocks.BeforeBlockRead = _ =>
            {
                if (Interlocked.Increment(ref blocksRead) != 1)
                    return;

                entered.TrySetResult();
                release.Task.Wait(Patience);
            };

            using var cancel = new CancellationTokenSource();
            var request = ReadAsync(host, "q", T0, T0.AddHours(1), token: cancel.Token);

            await entered.Task.WaitAsync(Patience);

            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

            await TestWait.UntilAsync(() => _tables.ReadToken.IsCancellationRequested, "the server to see the request aborted");
            release.SetResult();

            var ended = await _tables.ReadEnded.Task.WaitAsync(Patience);

            Assert.IsAssignableFrom<OperationCanceledException>(ended);
            Assert.Equal(1, Volatile.Read(ref blocksRead));
        }


        // === Helpers ===

        private Task<SensorApiTestHost> StartHost(Action<TableSensorsConfig> tables = null)
        {
            var config = new TableSensorsConfig();
            tables?.Invoke(config);

            return SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), tables: _tables, tableSensors: config);
        }

        // Rows with every kind of edge value: a null bid, a null size, NaN, the infinities, -0.0, the
        // extreme integers, a time with a tick in its last fraction digit.
        private byte[] Edges(string name) => Table(name,
            (T0, 1.5, long.MinValue),
            (T0.AddSeconds(1), null, 2),
            (T0.AddSeconds(2), double.NaN, null),
            (T0.AddSeconds(3), double.PositiveInfinity, 4),
            (T0.AddSeconds(4), double.NegativeInfinity, long.MaxValue),
            (T0.AddSeconds(5).AddTicks(1), -0.0, 6));

        private byte[] Table(string name, params (DateTime Time, double? Bid, long? Size)[] rows)
        {
            var bidNulls = new byte[TableNullMap.ByteLength(rows.Length)];
            var sizeNulls = new byte[TableNullMap.ByteLength(rows.Length)];

            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].Bid is null) TableNullMap.SetNull(bidNulls, i);
                if (rows[i].Size is null) TableNullMap.SetNull(sizeNulls, i);
            }

            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
            writer.AddTimeColumn(rows.Select(r => r.Time.Ticks).ToArray());
            writer.AddDoubleColumn(rows.Select(r => r.Bid ?? 0).ToArray(), bidNulls);
            writer.AddInt64Column(rows.Select(r => r.Size ?? 0).ToArray(), sizeNulls);
            return writer.ToArray();
        }

        private void Put(byte[] block)
        {
            var result = _blocks.Put(TestProductsManager.ProductId, _producer, ++_sequence, block);

            Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
            Assert.Empty(result.Refusals);
        }

        // One merged hour of the product holding one batch of the sensor whose bytes are not a batch. The hour is one of its
        // own (the store keeps a committed hour as it is), long before the rows' times: the arrival hour of a batch need
        // not be near its rows.
        private void WriteDamagedBatch(TableSensorModel sensor, DateTime first, DateTime last)
        {
            var hour = new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc).AddHours(Interlocked.Increment(ref _damagedHours)).Ticks;
            var period = TablePeriodClass.Of(sensor, DateTime.UtcNow);

            // As the merge does: the sensor's placement before its first batch (#1850 part 4b).
            _databaseCoreManager.DatabaseCore.TableBatches.SetPlacement(TestProductsManager.ProductId, sensor.Id, new TablePlacement(period));
            _databaseCoreManager.DatabaseCore.TableBatches.WriteHour(TestProductsManager.ProductId,
                new TableBatchHourCommit { HourTicks = hour, FirstBlockSequence = sensor.FirstSequence, LastBlockSequence = sensor.FirstSequence, BlocksMerged = 1 },
                [new TableBatchHourPart(period, [new TableBatchToWrite(sensor.Id, first.Ticks, last.Ticks, 2, [9, 9, 9])])]);
        }

        private static int _damagedHours;

        private TableRows Read(BaseSensorModel sensor) => _blocks.Read(sensor.Id, T0.AddDays(-1), T0.AddDays(1)).Value;

        private async Task<BaseSensorModel> RegisterAsync(string name, TableSchema schema = null)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = schema ?? Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor));
            return sensor;
        }

        private async Task CreateIntSensorAsync(string name)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, $"{_root}/{name}", DateTime.UtcNow);
            Assert.True((await _valuesCache.AddSensorValueAsync(Guid.Parse(_key), TestProductsManager.ProductId, value)).IsOk);
        }

        private Guid AddKey(KeyPermissions? permissions = null)
        {
            var entity = permissions is null
                ? EntitiesFactory.BuildAccessKeyEntity(productId: TestProductsManager.TestProduct.Id)
                : EntitiesFactory.BuildAccessKeyEntity(productId: TestProductsManager.TestProduct.Id, permissions: permissions.Value);

            return _valuesCache.AddAccessKey(new AccessKeyModel(entity)).Id;
        }

        private Guid Block(Guid key)
        {
            _valuesCache.UpdateAccessKeyState(key, KeyState.Blocked);
            return key;
        }

        private string Body(string name, DateTime from, DateTime to, int? maxRows = null, int format = 0) =>
            $"{{\"Path\":\"{_root}/{name}\",\"From\":\"{from:O}\",\"To\":\"{to:O}\",\"MaxRows\":{(maxRows is null ? "null" : maxRows.ToString())},\"Format\":{format}}}";

        private Task<Answer> ReadAsync(SensorApiTestHost host, string name, DateTime from, DateTime to, int? maxRows = null, int format = 0, CancellationToken token = default) =>
            PostAsync(host, Body(name, from, to, maxRows, format), _key, token);

        private static async Task<Answer> PostAsync(SensorApiTestHost host, string body, string key, CancellationToken token = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(Patience);

            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, "tableHistory"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", "table-read-tests");

            if (key is not null)
                request.Headers.Add("Key", key);

            using var response = await http.SendAsync(request, deadline.Token);

            var headers = response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);

            return new Answer(response.StatusCode, response.Content.Headers.ContentType?.ToString(), headers, await response.Content.ReadAsByteArrayAsync(deadline.Token));
        }

        // The blocks of an answer, one after another: each is 44 header bytes and its frame, whose length
        // is the int32 at offset 32 of the header (the format doc's header table).
        private static List<BlockTable> ReadBlocks(byte[] body)
        {
            var tables = new List<BlockTable>();

            for (var offset = 0; offset < body.Length;)
            {
                var length = 44 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset + 32));
                var block = TableBlockReader.Read(body.AsSpan(offset, length));

                Assert.Single(block.Tables);
                Assert.Equal(1, block.Header.FormatVersion);

                tables.Add(block.Tables[0]);
                offset += length;
            }

            return tables;
        }


        private sealed record Answer(HttpStatusCode Status, string ContentType, Dictionary<string, string> Headers, byte[] Body)
        {
            public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
        }
    }


    // The block service with what the read tests need: reads counted, the token and the end of the
    // last read kept, and a store fault on demand.
    internal sealed class ReadControlledTableBlocks(ITableBlockService inner) : ITableBlockService
    {
        private int _reads;


        public volatile bool ReadFault;

        public int Reads => Volatile.Read(ref _reads);

        public CancellationToken ReadToken { get; private set; }

        // Completes with what ended the last read: null when it returned, else its exception.
        public TaskCompletionSource<Exception> ReadEnded { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);


        public TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block) => inner.Put(productId, producerId, producerSequence, block);

        public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null, CancellationToken token = default)
        {
            Interlocked.Increment(ref _reads);
            ReadToken = token;

            if (ReadFault)
                throw new TableBlockStoreException("A store fault made by the test.");

            try
            {
                var result = inner.Read(sensorId, from, to, maxRows, token);
                ReadEnded.TrySetResult(null);
                return result;
            }
            catch (Exception ex)
            {
                ReadEnded.TrySetResult(ex);
                throw;
            }
        }
    }
}
