using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
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
using HSMCommon.TaskResult;
using System;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class TableConnectionFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableConnectionTests);
    }


    // The table connection of the collector session (#1845, ADR-0021 point 7) over real WebSockets:
    // a real ClientWebSocket speaking the contract byte by byte (TableConnectionTestClient) against
    // the real Sensor API on a real Kestrel listener, the real cache, and the real block service
    // and block store underneath.
    //
    // A refusal is asserted as the refusal the contract names (a 200 with the refused tables, a 400
    // for a block that cannot be read), a fault as a fault (503 with retryAfter, nothing stored).
    // Nothing waits for time to pass: a put is held at a gate of the block service, and a wait is
    // bounded only so that a failure ends.
    public class TableConnectionTests : MonitoringCoreTestsBase<TableConnectionFixture>
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
        private static readonly DateTime T0 = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _root = $"t{Guid.NewGuid():N}"[..12];
        private readonly Guid _producer = Guid.NewGuid();
        private readonly TableBlockService _blocks;
        private readonly ControlledTableBlocks _tables;


        public TableConnectionTests(TableConnectionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _blocks = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            _tables = new ControlledTableBlocks(_blocks);
        }


        // === The upgrade ===

        [Fact]
        public async Task Upgrade_WithALiveSessionOfTheSameKey_SaysHelloWithTheFrameAndBlockLimits()
        {
            await using var host = await StartHost(sessions => { sessions.MaxTableFrameBytes = 256 * 1024; sessions.HeartbeatSeconds = 17; });

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Matches("^[0-9a-f]{32}$", sessionId);

            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            Assert.Equal("hsm.tables.v1", table.Socket.SubProtocol);

            var body = table.Hello["body"];

            Assert.Equal(256 * 1024, (int)body["maxFrameBytes"]);
            Assert.Equal(17, (int)body["heartbeatSeconds"]);
            Assert.Equal(TableBlockLimits.Default.MaxDecompressedBytes, (int)body["blockLimits"]["maxDecompressedBytes"]);
            Assert.Equal(TableBlockLimits.Default.MaxTables, (int)body["blockLimits"]["maxTables"]);
            Assert.Equal(TableBlockLimits.Default.MaxRows, (int)body["blockLimits"]["maxRows"]);
            Assert.Equal(TableBlockLimits.Default.MaxColumnsPerTable, (int)body["blockLimits"]["maxColumnsPerTable"]);
            Assert.Equal(TableBlockLimits.Default.MaxTableIdLength, (int)body["blockLimits"]["maxTableIdLength"]);

            Assert.Equal(1, host.Registry.Count);
            Assert.Equal(1, host.Registry.TableConnectionCount);
            Assert.Single(host.Log.Matching("Collector table connection", $"of session {sessionId[..8]} opened"));
        }

        [Theory]
        [InlineData("none")]
        [InlineData("malformed")]
        [InlineData("unknown")]
        [InlineData("another key")]
        [InlineData("ended")]
        public async Task Upgrade_WithoutALiveSessionOfTheSameKey_Is409(string named)
        {
            await using var host = await StartHost();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            var key = _key;

            switch (named)
            {
                case "none":
                    sessionId = null;
                    break;
                case "malformed":
                    sessionId = "not-a-session";
                    break;
                case "unknown":
                    sessionId = Guid.NewGuid().ToString("N");
                    break;
                case "another key":
                    key = AddKey().ToString();
                    break;
                case "ended":
                    await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Deadline());
                    await TestWait.UntilAsync(() => host.Registry.Count == 0, "the session to end");
                    break;
            }

            Assert.Equal(HttpStatusCode.Conflict, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, key, sessionId));
            Assert.Equal(0, host.Registry.TableConnectionCount);
        }

        [Fact]
        public async Task Upgrade_WithoutAUsableKey_Is406()
        {
            await using var host = await StartHost();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Equal(HttpStatusCode.NotAcceptable, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, key: null, sessionId));
            Assert.Equal(HttpStatusCode.NotAcceptable, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, Block(AddKey()).ToString(), sessionId));
        }

        // MaxTableFrameBytes = 0 stops table ingest and nothing else.
        [Fact]
        public async Task TableConnectionsSwitchedOff_Is404_AndTheSessionGoesOn()
        {
            await using var host = await StartHost(sessions => sessions.MaxTableFrameBytes = 0);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Equal(HttpStatusCode.NotFound, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, _key, sessionId));

            await SendTextAsync(session, $"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue($"{_root}/value")}]}}");

            Assert.Equal(200, (int)(await TableConnectionTestClient.ReceiveAsync(session))["status"]);
            Assert.Single(host.Log.Matching("table connections are switched off"));
        }

        // A table connection takes no slot: the limits count collectors, one session each.
        [Fact]
        public async Task ATableConnection_TakesNoSlot()
        {
            await using var host = await StartHost(sessions => { sessions.MaxSessions = 1; sessions.MaxSessionsPerAddress = 1; });

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            // The address holds its one session; a second session is still refused for it.
            Assert.Equal(HttpStatusCode.TooManyRequests, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, _key, sessionId: null, "hsm.sensors.v1"));

            await table.SendProbeAsync(1);
            Assert.Equal(200, (await table.ReceiveAckAsync()).Status);
        }

        [Fact]
        public async Task ASecondTableConnection_ReplacesTheFirst()
        {
            await using var host = await StartHost();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            using var first = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);
            using var second = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            Assert.Equal(WebSocketCloseStatus.NormalClosure, await first.ExpectCloseAsync());
            Assert.Contains("replaced", first.Socket.CloseStatusDescription);

            await second.SendProbeAsync(1);
            Assert.Equal(200, (await second.ReceiveAckAsync()).Status);

            await TestWait.UntilAsync(() => host.Registry.TableConnectionCount == 1, "the replaced connection to end");
        }

        [Fact]
        public async Task TheEndOfItsSession_ClosesTheTableConnection_AndTheCloseLineCountsItsBlocks()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            await table.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));
            Assert.Equal("stored", (await table.ReceiveAckAsync()).Outcome);

            await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Deadline());
            session.Dispose();

            Assert.Equal(WebSocketCloseStatus.NormalClosure, await table.ExpectCloseAsync());
            Assert.Equal("its session ended", table.Socket.CloseStatusDescription);

            await TestWait.UntilAsync(() => host.Registry.TableConnectionCount == 0, "the table connection to end");
            await TestWait.UntilAsync(() => host.Log.Matching("Collector table connection", "closed after", "blocks stored 1, duplicate 0").Length == 1, "the close line");
        }


        // === Frames ===

        [Fact]
        public async Task AProbe_IsAnsweredWithAnOrdinaryAck()
        {
            await using var host = await StartHost();
            using var table = await OpenTableAsync(host);

            await table.SendProbeAsync(7);

            var ack = await table.ReceiveAsync();

            Assert.Equal("{\"ack\":7,\"status\":200,\"body\":{}}", ack.ToJsonString());
        }

        [Fact]
        public async Task ABlock_IsStored_AndReadBackValueForValue()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");
            using var table = await OpenTableAsync(host);

            var nulls = new byte[1];
            TableNullMap.SetNull(nulls, 2);

            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/q"), Quotes.Id, 4);
            writer.AddTimeColumn([T0.Ticks, T0.AddSeconds(1).Ticks, T0.AddSeconds(2).Ticks, T0.AddSeconds(3).Ticks]);
            writer.AddDoubleColumn([1.5, double.NaN, 0, -0.0], nulls);
            writer.AddInt64Column([long.MinValue, 2, 3, long.MaxValue]);

            await table.SendBlockAsync(1, _producer, 1, writer.ToArray());

            var ack = await table.ReceiveAckAsync();

            Assert.Equal(1, ack.Id);
            Assert.Equal(200, ack.Status);
            Assert.Equal("stored", ack.Outcome);
            Assert.Equal(1, ack.Tables);
            Assert.Empty(ack.Refused);

            var rows = Read(sensor);

            Assert.Equal([T0.Ticks, T0.AddSeconds(1).Ticks, T0.AddSeconds(2).Ticks, T0.AddSeconds(3).Ticks], rows.Time);
            Assert.Equal(BitConverter.DoubleToInt64Bits(1.5), BitConverter.DoubleToInt64Bits(rows.Columns[1].DoubleValues[0]));
            Assert.True(double.IsNaN(rows.Columns[1].DoubleValues[1]));
            Assert.True(rows.Columns[1].IsNull(2));
            Assert.False(rows.Columns[1].IsNull(3));
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(rows.Columns[1].DoubleValues[3]));
            Assert.Equal([long.MinValue, 2, 3, long.MaxValue], rows.Columns[2].Int64Values);
        }

        [Fact]
        public async Task ABlockWithARefusedTable_IsStored_AndTheRefusalNamesTheTableAndTheReason()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("known");
            using var table = await OpenTableAsync(host);

            await table.SendBlockAsync(1, _producer, 1, Block(("known", T0, 1.0), ("unknown", T0, 2.0)));

            var ack = await table.ReceiveAckAsync();

            Assert.Equal(200, ack.Status);
            Assert.Equal("stored", ack.Outcome);
            Assert.Equal(2, ack.Tables);
            Assert.Equal(new[] { (1, $"{_root}/unknown", "UnknownSensor") }, ack.Refused);
            Assert.Equal(1, Read(sensor).RowCount);
        }

        [Fact]
        public async Task ABlockWithEveryTableRefused_IsAnsweredAllRefused_WithEveryReason()
        {
            await using var host = await StartHost();
            await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, $"{_root}/other")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time), new TableSchemaColumn("ask", TableColumnType.Double)]),
            });

            using var table = await OpenTableAsync(host);

            await table.SendBlockAsync(1, _producer, 1, Block(("other", T0, 1.0), ("missing", T0, 2.0)));

            var ack = await table.ReceiveAckAsync();

            Assert.Equal(200, ack.Status);
            Assert.Equal("allRefused", ack.Outcome);
            Assert.Equal(new[] { (0, $"{_root}/other", "SchemaIdMismatch"), (1, $"{_root}/missing", "UnknownSensor") }, ack.Refused);
        }

        // A block that cannot be read, or a frame whose producer cannot be, is a final refusal: a
        // 400 with the reason, and the connection goes on.
        [Theory]
        [InlineData("checksum", "invalid block: ChecksumMismatch")]
        [InlineData("empty", "invalid block: Truncated")]
        [InlineData("no producer", "invalid producer: the producer id is empty")]
        [InlineData("negative sequence", "invalid producer: the producer sequence is negative")]
        public async Task AnInvalidBlock_Is400_AndTheConnectionGoesOn(string fault, string error)
        {
            await using var host = await StartHost();
            await RegisterAsync("q");
            using var table = await OpenTableAsync(host);

            var block = Block("q", (T0, 1.0, 1L));

            switch (fault)
            {
                case "checksum":
                    block[^1] ^= 0xFF;
                    await table.SendBlockAsync(1, _producer, 1, block);
                    break;
                case "empty":
                    await table.SendBlockAsync(1, _producer, 1, []);
                    break;
                case "no producer":
                    await table.SendBlockAsync(1, Guid.Empty, 1, block);
                    break;
                case "negative sequence":
                    await table.SendBlockAsync(1, _producer, -1, block);
                    break;
            }

            var ack = await table.ReceiveAckAsync();

            Assert.Equal(1, ack.Id);
            Assert.Equal(400, ack.Status);
            Assert.Equal(error, ack.Error);
            Assert.Null(ack.Outcome);
            Assert.Equal(0, _tables.Stored);

            await table.SendProbeAsync(2);
            Assert.Equal(200, (await table.ReceiveAckAsync()).Status);
        }

        // A header that cannot be trusted has no request id to answer: a protocol error.
        [Theory]
        [InlineData("short")]
        [InlineData("version")]
        [InlineData("kind")]
        [InlineData("reserved")]
        [InlineData("header length")]
        public async Task AFrameWithoutATrustedHeader_ClosesWith1002(string fault)
        {
            await using var host = await StartHost();
            using var table = await OpenTableAsync(host);

            var frame = TableConnectionTestClient.Frame(2, 1, _producer, 1, ReadOnlySpan<byte>.Empty);

            switch (fault)
            {
                case "short":
                    frame = frame[..39];
                    break;
                case "version":
                    frame[0] = 2;
                    break;
                case "kind":
                    frame[1] = 9;
                    break;
                case "reserved":
                    frame[3] = 1;
                    break;
                case "header length":
                    frame[4] = 39;
                    break;
            }

            await table.SendBinaryAsync(frame);

            Assert.Equal(WebSocketCloseStatus.ProtocolError, await table.ExpectCloseAsync());
        }

        [Fact]
        public async Task AFrameOverTheTableFrameLimit_ClosesWith1009()
        {
            await using var host = await StartHost(sessions => sessions.MaxTableFrameBytes = 64 * 1024);
            using var table = await OpenTableAsync(host);

            await table.SendBlockAsync(1, _producer, 1, new byte[64 * 1024]);

            Assert.Equal(WebSocketCloseStatus.MessageTooBig, await table.ExpectCloseAsync());
            Assert.Equal(0, _tables.Puts);
        }

        // The table connection carries blocks only; its heartbeat is the session's.
        [Fact]
        public async Task TextOnATableConnection_IsAHeartbeat_OrAnUnknownOp()
        {
            await using var host = await StartHost();
            using var table = await OpenTableAsync(host);

            await table.SendTextAsync("{\"op\":\"ping\"}");
            Assert.Equal("pong", (string)(await table.ReceiveAsync())["op"]);

            await table.SendTextAsync($"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue($"{_root}/value")}]}}");
            Assert.Equal("{\"ack\":1,\"status\":400,\"error\":\"unknown op\"}", (await table.ReceiveAsync()).ToJsonString());

            await table.SendTextAsync("{\"id\":2,\"op\":\"commands\",\"body\":[{\"Type\":0,\"Path\":\"x\",\"SensorType\":1}]}");
            Assert.Equal("{\"ack\":2,\"status\":400,\"error\":\"unknown op\"}", (await table.ReceiveAsync()).ToJsonString());

            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, $"{_root}/value", out _));
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "x", out _));
        }

        [Fact]
        public async Task ABinaryFrameOnTheOrdinaryConnection_StillClosesItWith1003()
        {
            await using var host = await StartHost();

            var (session, _) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _1 = session;

            await session.SendAsync(TableConnectionTestClient.Frame(2, 1, _producer, 1, ReadOnlySpan<byte>.Empty), WebSocketMessageType.Binary, true, Deadline());

            Assert.Equal(WebSocketCloseStatus.InvalidMessageType, await TableConnectionTestClient.ExpectCloseAsync(session));
        }

        [Fact]
        public async Task AKeyThatCanNoLongerSend_ClosesTheTableConnectionWith1008_AndTheBlockIsNotStored()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");
            var key = AddKey();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, key.ToString());
            using var _ = session;
            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, key.ToString(), sessionId);

            Block(key);

            await table.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));

            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await table.ExpectCloseAsync());
            Assert.Equal(0, _tables.Puts);
            Assert.Equal(0, Read(sensor).RowCount);
        }


        // === Delivery ===

        // The ack of a stored block was lost with its connection: the collector sends the same
        // block (producer id and sequence) on its next connection and is told it was a repeat.
        [Fact]
        public async Task ABlockSentAgainAfterALostAck_IsADuplicate_AndStoredOnce()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            var block = Block("q", (T0, 1.0, 1L), (T0.AddSeconds(1), 2.0, 2L));

            using (var first = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId))
            {
                await first.SendBlockAsync(1, _producer, 41, block);

                // Stored; the connection goes before its ack is read.
                await TestWait.UntilAsync(() => _tables.Stored == 1, "the block to be stored");
                first.Socket.Abort();
            }

            using var second = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            await second.SendBlockAsync(1, _producer, 41, block);

            var ack = await second.ReceiveAckAsync();

            Assert.Equal(200, ack.Status);
            Assert.Equal("duplicate", ack.Outcome);
            Assert.Equal(1, ack.Tables);
            Assert.Empty(ack.Refused);
            Assert.Equal(2, Read(sensor).RowCount);
        }

        // A replaced connection still storing its block while the collector sends the same block on
        // the new connection: both puts run inside the block service at once. The first put holds
        // its reserved sequence at a gate; the resend reserves the next one, and the store appends
        // in sequence order, so the resend's append waits for the first and finds it stored. The
        // replaced connection is closed only after its ack.
        [Fact]
        public async Task AReplacedConnectionsPut_RacingTheResend_StoresTheBlockOnce()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            var block = Block("q", (T0, 1.0, 1L));
            var gate = HoldTheFirstPutAfterItsReservation();

            using var first = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            await first.SendBlockAsync(1, _producer, 7, block);
            await gate.Entered.Task.WaitAsync(Patience);

            using var second = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            // The replaced connection is storing: its close waits for the ack.
            await TestWait.UntilAsync(() => host.Registry.AnyDeferredClose, "the replaced connection to defer its close");

            await second.SendBlockAsync(1, _producer, 7, block);

            // The resend is inside the service too, behind the held reservation.
            await TestWait.UntilAsync(() => _tables.Inside == 2, "the resend to be put while the first put is held");

            gate.Release.SetResult();

            var firstAck = await first.ReceiveAckAsync();
            var secondAck = await second.ReceiveAckAsync();

            Assert.Equal("stored", firstAck.Outcome);
            Assert.Equal("duplicate", secondAck.Outcome);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, await first.ExpectCloseAsync());
            Assert.Contains("replaced", first.Socket.CloseStatusDescription);

            Assert.Equal(1, Read(sensor).RowCount);
            Assert.Equal(1, _tables.Stored);
        }

        // A large block on the table connection does not hold back the ordinary connection.
        [Fact]
        public async Task AHeldPut_DoesNotDelayAnAckOnTheOrdinaryConnection()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;
            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            var gate = HoldTheFirstPutAfterItsReservation();

            await table.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));
            await gate.Entered.Task.WaitAsync(Patience);

            await SendTextAsync(session, $"{{\"id\":5,\"op\":\"list\",\"body\":[{IntValue($"{_root}/value")}]}}");

            var listAck = await TableConnectionTestClient.ReceiveAsync(session);

            Assert.Equal(5, (long)listAck["ack"]);
            Assert.Equal(200, (int)listAck["status"]);
            Assert.False(gate.Release.Task.IsCompleted);

            gate.Release.SetResult();

            Assert.Equal("stored", (await table.ReceiveAckAsync()).Outcome);
        }

        // A fault of the store is not a refusal: nothing was stored, the collector keeps the block
        // and sends it again after the time the ack names.
        [Fact]
        public async Task AStoreFault_Is503WithRetryAfter_AndTheResendIsStoredOnce()
        {
            await using var host = await StartHost();
            var sensor = await RegisterAsync("q");
            using var table = await OpenTableAsync(host);

            var block = Block("q", (T0, 1.0, 1L));

            _tables.Fault = true;

            await table.SendBlockAsync(1, _producer, 3, block);

            var fault = await table.ReceiveAckAsync();

            Assert.Equal(503, fault.Status);
            Assert.Equal(60, fault.RetryAfter);
            Assert.Equal("the block store cannot take blocks now", fault.Error);
            Assert.Null(fault.Outcome);
            Assert.Equal(0, Read(sensor).RowCount);

            _tables.Fault = false;

            await table.SendBlockAsync(2, _producer, 3, block);

            var stored = await table.ReceiveAckAsync();

            Assert.Equal(200, stored.Status);
            Assert.Equal("stored", stored.Outcome);
            Assert.Equal(1, Read(sensor).RowCount);
        }

        [Fact]
        public async Task ShutdownDuringAPut_AcknowledgesTheBlockBeforeClosing()
        {
            var host = await StartHost();
            var sensor = await RegisterAsync("q");

            try
            {
                using var table = await OpenTableAsync(host);

                var gate = HoldTheFirstPutAfterItsReservation();

                await table.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));
                await gate.Entered.Task.WaitAsync(Patience);

                var stop = Task.Run(host.StopAsync);

                await TestWait.UntilAsync(() => host.Registry.AnyDeferredClose, "the shutdown to ask the busy table connection to close");
                gate.Release.SetResult();

                Assert.Equal("stored", (await table.ReceiveAckAsync()).Outcome);
                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await table.ExpectCloseAsync());

                await stop.WaitAsync(Patience);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.Equal(1, Read(sensor).RowCount);
        }

        // MaxConcurrentPuts: a block that finds every turn taken waits for one, unanswered.
        [Fact]
        public async Task ABlockBeyondMaxConcurrentPuts_WaitsForATurn()
        {
            await using var host = await StartHost(tableSensors: tables => tables.MaxConcurrentPuts = 1);
            var sensor = await RegisterAsync("q");

            using var first = await OpenTableAsync(host);
            using var second = await OpenTableAsync(host);

            var gate = HoldTheFirstPutAfterItsReservation();

            await first.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));
            await gate.Entered.Task.WaitAsync(Patience);

            await second.SendBlockAsync(1, Guid.NewGuid(), 1, Block("q", (T0.AddSeconds(1), 2.0, 2L)));

            // The second frame has arrived (its request is counted before the turn is taken).
            await TestWait.UntilAsync(() => host.Telemetry.Records.Count(record => record.Kind == "request") >= 2, "the second block to arrive");

            Assert.Equal(1, _tables.Inside);

            gate.Release.SetResult();

            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
            Assert.Equal("stored", (await second.ReceiveAckAsync()).Outcome);
            Assert.Equal(1, _tables.MostInside);
            Assert.Equal(2, Read(sensor).RowCount);
        }

        // Two collectors with one producer id: said once a minute, not refused.
        [Fact]
        public async Task OneProducerIdOnTwoLiveConnections_IsAWarning_NotARefusal()
        {
            await using var host = await StartHost();
            await RegisterAsync("q");

            using var first = await OpenTableAsync(host);
            using var second = await OpenTableAsync(host);

            await first.SendBlockAsync(1, _producer, 1, Block("q", (T0, 1.0, 1L)));
            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);

            await second.SendBlockAsync(1, _producer, 2, Block("q", (T0.AddSeconds(1), 2.0, 2L)));
            Assert.Equal("stored", (await second.ReceiveAckAsync()).Outcome);

            await second.SendBlockAsync(2, _producer, 3, Block("q", (T0.AddSeconds(2), 3.0, 3L)));
            Assert.Equal("stored", (await second.ReceiveAckAsync()).Outcome);

            var warnings = host.Log.Matching("Two live table connections", $"one producer id {_producer}");

            Assert.Single(warnings);
            Assert.Equal(NLog.LogLevel.Warn, warnings[0].Level);
        }


        // === The table frame budget ===

        // The budget holds one large frame (CollectorSessions.MaxTableBytesInFlight = what one
        // reservation needs): a second connection's large frame waits, unread, while the first one's
        // put is held, and goes through when the first is done.
        [Fact]
        public async Task TheBudget_HoldsASecondLargeFrame_UntilTheFirstOnesPutIsDone()
        {
            await using var host = await StartBudgetHost();
            var sensor = await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            using var first = await OpenTableAsync(host);
            using var second = await OpenTableAsync(host);

            var hold = _tables.HoldNextPut();

            await first.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));
            await hold.Entered.Task.WaitAsync(Patience);

            Assert.Equal(BudgetReservation, budget.InUse);

            var sending = second.SendBlockAsync(1, Guid.NewGuid(), 1, LargeBlock("q", 2));

            await TestWait.UntilAsync(() => budget.Waiting == 1, "the second large frame to wait for the budget");
            Assert.Equal(1, _tables.Puts);
            Assert.Single(host.Log.Matching("The table frame budget is spent"));

            hold.Release.SetResult();

            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
            Assert.Equal("stored", (await second.ReceiveAckAsync()).Outcome);
            await sending;

            await TestWait.UntilAsync(() => budget.InUse == 0 && budget.Waiting == 0, "the budget to be whole again");
            Assert.Equal(2 * LargeRows, Read(sensor).RowCount);
        }

        // A frame within the free allowance (past the session's own 4 KiB, within 64 KiB) takes
        // nothing from the budget: an exhausted budget does not delay it.
        [Fact]
        public async Task ASmallFrame_IsNotDelayedByASpentBudget()
        {
            await using var host = await StartBudgetHost();
            await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            using var first = await OpenTableAsync(host);
            using var second = await OpenTableAsync(host);
            using var third = await OpenTableAsync(host);

            var hold = _tables.HoldNextPut();

            await first.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));
            await hold.Entered.Task.WaitAsync(Patience);

            var sending = second.SendBlockAsync(1, Guid.NewGuid(), 1, LargeBlock("q", 2));
            await TestWait.UntilAsync(() => budget.Waiting == 1, "the second large frame to wait for the budget");

            var small = LargeBlock("q", 3, rows: 2_000);

            Assert.InRange(small.Length + 40, 4 * 1024 + 1, 64 * 1024);

            await third.SendBlockAsync(1, Guid.NewGuid(), 1, small);

            Assert.Equal("stored", (await third.ReceiveAckAsync()).Outcome);
            Assert.Equal(1, budget.Waiting);

            hold.Release.SetResult();

            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
            Assert.Equal("stored", (await second.ReceiveAckAsync()).Outcome);
            await sending;

            await TestWait.UntilAsync(() => budget.InUse == 0, "the budget to be whole again");
        }

        // The accounting on every way a frame can end.

        [Fact]
        public async Task TheBudget_IsWholeAgain_AfterAConnectionDropsMidFrame()
        {
            await using var host = await StartBudgetHost();
            var budget = host.Handler.TableBudget;

            using var table = await OpenTableAsync(host);

            var frame = TableConnectionTestClient.Frame(1, 1, _producer, 1, new byte[200 * 1024]);

            await table.Socket.SendAsync(frame.AsMemory(0, 150 * 1024), WebSocketMessageType.Binary, endOfMessage: false, Deadline());
            await TestWait.UntilAsync(() => budget.InUse == BudgetReservation, "the frame to reserve its bytes");

            table.Socket.Abort();

            await TestWait.UntilAsync(() => budget.InUse == 0 && host.Registry.TableConnectionCount == 0, "the reservation to go back with the connection");
        }

        [Fact]
        public async Task TheBudget_IsWholeAgain_AfterAFrameOverTheLimit()
        {
            await using var host = await StartBudgetHost();
            var budget = host.Handler.TableBudget;

            using var table = await OpenTableAsync(host);

            await table.SendBlockAsync(1, _producer, 1, new byte[BudgetFrameBytes]);

            Assert.Equal(WebSocketCloseStatus.MessageTooBig, await table.ExpectCloseAsync());
            await TestWait.UntilAsync(() => budget.InUse == 0, "the reservation to go back");
        }

        [Fact]
        public async Task TheBudget_IsWholeAgain_AfterAPutThatThrows()
        {
            await using var host = await StartBudgetHost();
            await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            using var table = await OpenTableAsync(host);

            _tables.Throw = true;

            await table.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));

            var ack = await table.ReceiveAckAsync();

            Assert.Equal(500, ack.Status);
            Assert.Equal("failed to store the block", ack.Error);
            await TestWait.UntilAsync(() => budget.InUse == 0, "the reservation to go back");
        }

        // A connection waiting for the budget is closed with its session: the wait ends and takes
        // nothing.
        [Fact]
        public async Task TheBudget_IsWholeAgain_AfterAWaitingConnectionsSessionEnds()
        {
            await using var host = await StartBudgetHost();
            await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            using var first = await OpenTableAsync(host);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var second = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            var hold = _tables.HoldNextPut();

            await first.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));
            await hold.Entered.Task.WaitAsync(Patience);

            var sending = second.SendBlockAsync(1, Guid.NewGuid(), 1, LargeBlock("q", 2));
            await TestWait.UntilAsync(() => budget.Waiting == 1, "the second large frame to wait for the budget");

            await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Deadline());
            session.Dispose();

            // The wait ends with the connection's close, before the collector answers it.
            await TestWait.UntilAsync(() => budget.Waiting == 0, "the wait to end with the session");
            Assert.Equal(BudgetReservation, budget.InUse);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, await second.ExpectCloseAsync());

            hold.Release.SetResult();

            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
            await TestWait.UntilAsync(() => budget.InUse == 0, "the budget to be whole again");
            await IgnoreAsync(sending);
        }

        // A wait past its bound (twice the silence limit) closes the connection with 1013: mid-frame
        // there is no request id to answer, so the collector sends the block again on its next
        // table connection.
        [Fact]
        public async Task AWaitPastItsBound_ClosesWith1013_AndTakesNothing()
        {
            await using var host = await StartBudgetHost();
            await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            using var first = await OpenTableAsync(host);
            using var second = await OpenTableAsync(host);

            var hold = _tables.HoldNextPut();

            await first.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));
            await hold.Entered.Task.WaitAsync(Patience);

            var sending = second.SendBlockAsync(1, Guid.NewGuid(), 1, LargeBlock("q", 2));
            await TestWait.UntilAsync(() => budget.Waiting == 1, "the second large frame to wait for the budget");

            var bound = new CollectorSessionsConfig().SilenceLimit * 2;
            ManualTimer timer = null;

            await TestWait.UntilAsync(() => (timer = host.Time.Timers.FirstOrDefault(t => t.DueTime == bound)) is not null, "the bound of the wait to be set");

            timer.Fire();

            Assert.Equal((WebSocketCloseStatus)1013, await second.ExpectCloseAsync());
            Assert.Equal(0, budget.Waiting);

            hold.Release.SetResult();

            Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
            await TestWait.UntilAsync(() => budget.InUse == 0, "the budget to be whole again");
            await IgnoreAsync(sending);
        }

        [Fact]
        public async Task TheBudget_IsWholeAgain_AfterAShutdownDuringAWait()
        {
            var host = await StartBudgetHost();
            await RegisterAsync("q");
            var budget = host.Handler.TableBudget;

            try
            {
                using var first = await OpenTableAsync(host);
                using var second = await OpenTableAsync(host);

                var hold = _tables.HoldNextPut();

                await first.SendBlockAsync(1, _producer, 1, LargeBlock("q", 1));
                await hold.Entered.Task.WaitAsync(Patience);

                var sending = second.SendBlockAsync(1, Guid.NewGuid(), 1, LargeBlock("q", 2));
                await TestWait.UntilAsync(() => budget.Waiting == 1, "the second large frame to wait for the budget");

                var stop = Task.Run(host.StopAsync);

                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await second.ExpectCloseAsync());

                hold.Release.SetResult();

                Assert.Equal("stored", (await first.ReceiveAckAsync()).Outcome);
                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await first.ExpectCloseAsync());

                await stop.WaitAsync(Patience);
                await IgnoreAsync(sending);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.Equal(0, budget.InUse);
            Assert.Equal(0, budget.Waiting);
        }


        // === Helpers ===

        private const int BudgetFrameBytes = 256 * 1024;
        private const long BudgetReservation = 2L * BudgetFrameBytes + 1;
        private const int LargeRows = 10_000;

        // Frames of at most 256 KiB, and a budget of exactly one reservation.
        private Task<SensorApiTestHost> StartBudgetHost() =>
            StartHost(sessions => { sessions.MaxTableFrameBytes = BudgetFrameBytes; sessions.MaxTableBytesInFlight = BudgetReservation; });

        // A block of one table whose frame is past the free allowance (64 KiB) and within 256 KiB:
        // random values do not compress.
        private byte[] LargeBlock(string name, int seed, int rows = LargeRows)
        {
            var random = new Random(seed);
            var times = Enumerable.Range(0, rows).Select(i => T0.AddMilliseconds(seed * 100_000 + i).Ticks).ToArray();

            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows);
            writer.AddTimeColumn(times);
            writer.AddDoubleColumn(Enumerable.Range(0, rows).Select(_ => random.NextDouble()).ToArray());
            writer.AddInt64Column(Enumerable.Range(0, rows).Select(_ => (long)random.Next(1000)).ToArray());

            var block = writer.ToArray();

            if (rows == LargeRows)
                Assert.InRange(block.Length + 40, 64 * 1024 + 1, BudgetFrameBytes);

            return block;
        }

        // A send the server never read to its end fails when the connection goes; that is expected.
        private static async Task IgnoreAsync(Task sending)
        {
            try
            {
                await sending.WaitAsync(Patience);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        private Task<SensorApiTestHost> StartHost(Action<CollectorSessionsConfig> sessions = null, Action<TableSensorsConfig> tableSensors = null)
        {
            var sessionsConfig = new CollectorSessionsConfig();
            sessions?.Invoke(sessionsConfig);

            var tablesConfig = new TableSensorsConfig();
            tableSensors?.Invoke(tablesConfig);

            return SensorApiTestHost.StartAsync(_valuesCache, sessionsConfig, tables: _tables, tableSensors: tablesConfig);
        }

        // A session of its own and its table connection. The client keeps the session's socket: one
        // left to the garbage collector is closed by its finalizer, and its table connection with it.
        private async Task<TableConnectionTestClient> OpenTableAsync(SensorApiTestHost host)
        {
            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);

            var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            table.Session = session;
            return table;
        }

        private (TaskCompletionSource Entered, TaskCompletionSource Release) HoldTheFirstPutAfterItsReservation()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            _blocks.AfterReserve = _ =>
            {
                if (Interlocked.Increment(ref calls) != 1)
                    return;

                entered.TrySetResult();
                release.Task.Wait(Patience);
            };

            return (entered, release);
        }

        private async Task<BaseSensorModel> RegisterAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor));
            return sensor;
        }

        private TableRows Read(BaseSensorModel sensor)
        {
            var result = _blocks.Read(sensor.Id, T0.AddHours(-1), T0.AddHours(1));
            Assert.True(result.IsOk, result.Error);
            return result.Value;
        }

        // One table of the Quotes schema.
        private byte[] Block(string name, params (DateTime Time, double Bid, long Size)[] rows)
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
            writer.AddTimeColumn(rows.Select(r => r.Time.Ticks).ToArray());
            writer.AddDoubleColumn(rows.Select(r => r.Bid).ToArray());
            writer.AddInt64Column(rows.Select(r => r.Size).ToArray());
            return writer.ToArray();
        }

        // Several tables of the Quotes schema, one row each.
        private byte[] Block(params (string Name, DateTime Time, double Bid)[] tables)
        {
            using var writer = new TableBlockWriter();

            foreach (var (name, time, bid) in tables)
            {
                writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, 1);
                writer.AddTimeColumn([time.Ticks]);
                writer.AddDoubleColumn([bid]);
                writer.AddInt64Column([1L]);
            }

            return writer.ToArray();
        }

        private Guid AddKey()
        {
            var entity = EntitiesFactory.BuildAccessKeyEntity(productId: TestProductsManager.TestProduct.Id);

            return _valuesCache.AddAccessKey(new AccessKeyModel(entity)).Id;
        }

        private Guid Block(Guid key)
        {
            _valuesCache.UpdateAccessKeyState(key, KeyState.Blocked);

            return key;
        }

        private static Task SendTextAsync(ClientWebSocket socket, string frame) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, Deadline());

        private static string IntValue(string path) =>
            $"{{\"Type\":1,\"Value\":1,\"Comment\":null,\"Time\":\"{DateTime.UtcNow:O}\",\"Status\":1,\"Key\":null,\"Path\":\"{path}\"}}";

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;
    }


    // The block service as the table connection sees it, with what the tests need to watch and
    // break: puts counted (inside now, the most at once, stored), and a store fault on demand.
    internal sealed class ControlledTableBlocks(ITableBlockService inner) : ITableBlockService
    {
        private int _inside;
        private int _mostInside;
        private int _puts;
        private int _stored;
        private int _holdTaken;


        public volatile bool Fault;

        // A put that throws what is not a store fault: an error of the server.
        public volatile bool Throw;

        private (TaskCompletionSource Entered, TaskCompletionSource Release)? _hold;


        // Holds the next put before it reaches the store (no sequence is reserved, so other puts
        // are not ordered behind it) until Release.
        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNextPut()
        {
            var hold = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            _hold = hold;
            return hold;
        }

        public int Inside => Volatile.Read(ref _inside);

        public int MostInside => Volatile.Read(ref _mostInside);

        public int Puts => Volatile.Read(ref _puts);

        public int Stored => Volatile.Read(ref _stored);


        public TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block)
        {
            Interlocked.Increment(ref _puts);

            if (Fault)
                throw new TableBlockStoreException("A store fault made by the test.");

            if (Throw)
                throw new InvalidOperationException("An error made by the test.");

            if (_hold is { } hold && Interlocked.CompareExchange(ref _holdTaken, 1, 0) == 0)
            {
                hold.Entered.TrySetResult();
                hold.Release.Task.Wait(TimeSpan.FromSeconds(10));
            }

            var inside = Interlocked.Increment(ref _inside);

            for (var most = Volatile.Read(ref _mostInside); inside > most; most = Volatile.Read(ref _mostInside))
                Interlocked.CompareExchange(ref _mostInside, inside, most);

            try
            {
                var result = inner.Put(productId, producerId, producerSequence, block);

                if (result.Outcome == TableBlockPutOutcome.Stored)
                    Interlocked.Increment(ref _stored);

                return result;
            }
            finally
            {
                Interlocked.Decrement(ref _inside);
            }
        }

        public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null) => inner.Read(sensorId, from, to, maxRows);
    }
}
