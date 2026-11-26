using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.SensorApi.Sessions.Files;
using HSMServer.ServerConfiguration;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class FileConnectionFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(FileConnectionTests);
    }


    // The file connection of the collector session (#1884, ADR-0022 points 6-8) over real WebSockets: a
    // real ClientWebSocket speaking the contract byte by byte (FileConnectionTestClient) against the real
    // Sensor API on a real Kestrel listener and the real cache, with the store of large files faked
    // (FakeFileUploadStore): the store is step 1's (#1883), and the connection is tested against its
    // interface. The end check is the connection's own, run over the pieces the fake keeps.
    //
    // The issue's test "the budget and its 1013" is replaced, on purpose, by the bound on uploads in
    // progress: a file connection has no frame budget to wait for and never closes with 1013.
    public class FileConnectionTests : MonitoringCoreTestsBase<FileConnectionFixture>
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _path = $"f{Guid.NewGuid():N}"[..12] + "/dumps";
        private readonly FakeFileUploadStore _store = new();


        public FileConnectionTests(FileConnectionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        // === The upgrade ===

        [Fact]
        public async Task Upgrade_SaysHelloWithTheFrameAndFileLimits()
        {
            await using var host = await StartHost(sessions => { sessions.MaxFileFrameBytes = 1024 * 1024; sessions.HeartbeatSeconds = 17; });
            _store.MaxFileBytes = 123_456_789;

            using var file = await OpenFileAsync(host);

            Assert.Equal("hsm.files.v1", file.Socket.SubProtocol);

            var body = file.Hello["body"];

            Assert.Equal(1024 * 1024, (int)body["maxFrameBytes"]);
            Assert.Equal(17, (int)body["heartbeatSeconds"]);
            Assert.Equal(123_456_789, (long)body["fileLimits"]["maxFileBytes"]);
            Assert.Equal(FilePiece.PieceBytesFor(1024 * 1024), (int)body["fileLimits"]["pieceBytes"]);

            Assert.Equal(1, host.Registry.FileConnectionCount);
            Assert.Single(host.Log.Matching("Collector file connection", "opened"));
        }

        [Fact]
        public void ThePieceSize_FitsAFrameWithItsHeaderAndTheWorstZstdGrowth()
        {
            Assert.Equal(4_173_824, FilePiece.PieceBytesFor(CollectorSessionsConfig.DefaultMaxFileFrameBytes));

            foreach (var frame in new[] { 128 * 1024, 256 * 1024 + 7, 4 * 1024 * 1024, 64 * 1024 * 1024 })
            {
                var piece = FilePiece.PieceBytesFor(frame);

                Assert.True(piece >= FilePiece.MinPieceBytes, $"{frame}: {piece}");
                Assert.Equal(0, piece % 4096);
                Assert.True(FilePiece.HeaderLength + FilePiece.CompressBound(piece) <= frame, $"{frame}: {piece}");
                Assert.True(FilePiece.HeaderLength + FilePiece.CompressBound(piece + 4096) > frame, $"{frame}: {piece} is not the largest");
            }
        }

        [Theory]
        [InlineData("none")]
        [InlineData("unknown")]
        [InlineData("another key")]
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
                case "unknown":
                    sessionId = Guid.NewGuid().ToString("N");
                    break;
                case "another key":
                    key = AddKey().ToString();
                    break;
            }

            Assert.Equal(HttpStatusCode.Conflict, await FileConnectionTestClient.RefusedStatusAsync(host.SessionUri, key, sessionId));
            Assert.Equal(0, host.Registry.FileConnectionCount);
        }

        [Fact]
        public async Task Upgrade_WithoutAUsableKey_Is406()
        {
            await using var host = await StartHost();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Equal(HttpStatusCode.NotAcceptable, await FileConnectionTestClient.RefusedStatusAsync(host.SessionUri, key: null, sessionId));
            Assert.Equal(HttpStatusCode.NotAcceptable, await FileConnectionTestClient.RefusedStatusAsync(host.SessionUri, Block(AddKey()).ToString(), sessionId));
        }

        // MaxFileFrameBytes = 0, or a server without a store of large files: 404, and the session goes on.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FileConnectionsSwitchedOff_Is404_AndTheSessionGoesOn(bool noStore)
        {
            await using var host = noStore
                ? await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig())
                : await StartHost(sessions => sessions.MaxFileFrameBytes = 0);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Equal(HttpStatusCode.NotFound, await FileConnectionTestClient.RefusedStatusAsync(host.SessionUri, _key, sessionId));

            await session.SendAsync(Encoding.UTF8.GetBytes("{\"op\":\"ping\"}"), WebSocketMessageType.Text, true, Deadline());
            Assert.Equal("pong", (string)(await TableConnectionTestClient.ReceiveAsync(session))["op"]);
            Assert.Single(host.Log.Matching("file connections are switched off"));
        }

        [Fact]
        public async Task Upgrade_OfferingBothPairedSubprotocols_Is400()
        {
            await using var host = await StartHost();

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            Assert.Equal(HttpStatusCode.BadRequest, await FileConnectionTestClient.RefusedStatusAsync(host.SessionUri, _key, sessionId, "hsm.files.v1", "hsm.tables.v1"));
        }

        // No slot: one session holds its session, its table connection and its file connection.
        [Fact]
        public async Task AFileConnection_TakesNoSlot_AndASessionHoldsThreeSockets()
        {
            await using var host = await StartHost(sessions => { sessions.MaxSessions = 1; sessions.MaxSessionsPerAddress = 1; });

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            using var table = await TableConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);
            using var file = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            Assert.Equal(HttpStatusCode.TooManyRequests, await TableConnectionTestClient.RefusedStatusAsync(host.SessionUri, _key, sessionId: null, "hsm.sensors.v1"));

            Assert.Equal(1, host.Registry.Count);
            Assert.Equal(1, host.Registry.TableConnectionCount);
            Assert.Equal(1, host.Registry.FileConnectionCount);

            await file.SendBinaryAsync(Probe(7));
            Assert.Equal("{\"ack\":7,\"status\":200,\"body\":{}}", (await file.ReceiveAsync()).ToJsonString());
        }

        // A replaced connection gives its upload back before its successor says hello: the collector's
        // begin on the new connection does not find the file busy with its own old connection.
        [Fact]
        public async Task ASecondFileConnection_ReplacesTheFirst_AndItsUploadIsFreeAtTheHello()
        {
            await using var host = await StartHost();
            var data = TestFile.Random("a.dmp", 200_000);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            using var first = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            var begun = await first.BeginAsync(data, _path);
            Assert.Equal(200, (await first.SendPieceAsync(data.Piece(0), begun.Upload)).Status);

            // The collector answers the close of its old connection while it opens the new one.
            var closing = first.ExpectCloseAsync();

            using var second = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            Assert.Equal(WebSocketCloseStatus.NormalClosure, await closing);
            Assert.Contains("replaced", first.Socket.CloseStatusDescription);
            Assert.Equal(0, _store.LiveLeases);

            var again = await second.BeginAsync(data, _path);

            Assert.Equal(200, again.Status);
            Assert.Equal(1, again.Next);
            Assert.Equal(begun.Upload, again.Upload);
        }

        // A collector that no longer reads its old connection: the old one is cut off after the shutdown
        // budget, and its upload is free all the same when the new one says hello.
        [Fact]
        public async Task AReplacedConnectionThatDoesNotAnswer_IsCutOff_BeforeTheNewHello()
        {
            await using var host = await StartHost();
            var data = TestFile.Random("a.dmp", 200_000);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var _ = session;

            using var first = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            var begun = await first.BeginAsync(data, _path);
            Assert.Equal(200, (await first.SendPieceAsync(data.Piece(0), begun.Upload)).Status);

            using var second = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            Assert.Equal(0, _store.LiveLeases);
            Assert.Equal(1, (await second.BeginAsync(data, _path)).Next);
        }

        [Fact]
        public async Task TheEndOfItsSession_ClosesTheFileConnection_AndGivesItsUploadBack()
        {
            await using var host = await StartHost();
            var data = TestFile.Random("a.dmp", 200_000);

            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);
            using var file = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            var begun = await file.BeginAsync(data, _path);
            Assert.Equal(200, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Status);

            await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Deadline());
            session.Dispose();

            Assert.Equal(WebSocketCloseStatus.NormalClosure, await file.ExpectCloseAsync());
            Assert.Equal("its session ended", file.Socket.CloseStatusDescription);

            await TestWait.UntilAsync(() => host.Registry.FileConnectionCount == 0, "the file connection to end");
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the lease to go back");
            await TestWait.UntilAsync(() => host.Log.Matching("Collector file connection", "closed after", "pieces stored 1 (", "uploads begun 1, stored 0").Length == 1, "the close line");
        }


        // === Begin, pieces, end ===

        [Fact]
        public async Task AFile_IsBegunSentAndStored_AndTheValueHashIsOfTheOriginalBytes()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 3 * 64 * 1024 + 1000, seed: 7);

            var begun = await file.BeginAsync(data, _path);

            Assert.Equal(200, begun.Status);
            Assert.Equal(0, begun.Next);
            Assert.Equal(4, begun.Pieces);
            Assert.Equal(64 * 1024, begun.PieceBytes);
            Assert.Matches("^[0-9a-f]{32}$", begun.Upload);

            for (var i = 0; i < 4; i++)
                Assert.Equal(i + 1, (await file.SendPieceAsync(data.Piece(i), begun.Upload)).Next);

            // A quick check is answered with its outcome at once.
            var stored = await file.EndAsync(begun.Upload, data.Hash());

            Assert.Equal(200, stored.Status);
            Assert.Equal("stored", stored.State);
            Assert.Equal("2026-11-25T12:00:00.0000000Z", (string)stored.Body["time"]);

            var entry = _store.Find("app.dmp");

            Assert.Equal(FakeFileUploadStore.State.Verified, entry.State);
            Assert.Equal(SHA256.HashData(data.Bytes), entry.ContentHash);

            // The check gave the lease back; the connection holds no upload.
            await TestWait.UntilAsync(() => _store.LiveLeases == 0 && host.Handler.FileChecksRunning == 0, "the check to give the lease back");

            // A repeat is answered from the check, and a begin of the same file says it is stored.
            Assert.Equal("stored", (await file.EndAsync(begun.Upload, data.Hash())).State);

            var again = await file.BeginAsync(data, _path);

            Assert.Equal(200, again.Status);
            Assert.Equal("stored", again.State);

            await file.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Deadline());
            await TestWait.UntilAsync(() => host.Log.Matching("Collector file connection", "closed after", "pieces stored 4 (", "uploads begun 1, stored 1, discarded 0").Length == 1, "the close line");
            Assert.Single(host.Log.Matching($"upload {begun.Upload} of \"app.dmp\"", "is stored"));
        }

        // The retry time of a running check: about the time it reads the file at 256 MiB a second, 1..30 s.
        [Theory]
        [InlineData(0, 1)]
        [InlineData(100_000, 1)]
        [InlineData(300L * 1024 * 1024, 2)]
        [InlineData(64L * 1024 * 1024 * 1024, 30)]
        public void TheRetryTimeOfACheck_FollowsTheSize(long size, int retryAfter) => Assert.Equal(retryAfter, FileConnection.CheckRetryAfter(size));

        // A check longer than the quick wait is answered 202, with the retry time of its size, and asked again.
        [Fact]
        public async Task ALongCheck_Is202WithTheRetryTimeOfItsSize()
        {
            const int retryAfter = 1;

            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            var checking = await file.EndAsync(begun.Upload, data.Hash());

            Assert.Equal(202, checking.Status);
            Assert.Equal("checking", checking.State);
            Assert.Equal(retryAfter, checking.RetryAfter);

            gate.Release.SetResult();

            Assert.Equal("stored", (await file.EndUntilSettledAsync(begun.Upload, data.Hash())).State);
        }

        // One connection carries several files in turn: begin again after an end.
        [Fact]
        public async Task OneConnection_CarriesSeveralFilesInTurn()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            for (var n = 0; n < 3; n++)
                Assert.Equal("stored", (await file.UploadAsync(TestFile.Random($"f{n}.bin", 70_000, seed: n), _path)).State);

            Assert.Equal(3, Enumerable.Range(0, 3).Count(n => _store.Find($"f{n}.bin")?.State == FakeFileUploadStore.State.Verified));
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the checks to give their leases back");
        }

        // The mark of ADR-0022 point 20: absent means secure; another mark is another upload.
        [Theory]
        [InlineData(null, true)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task TheSecureMark_ReachesTheStore_AndAbsentIsSecure(bool? sent, bool kept)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = new TestFile { Name = "a.dmp", Bytes = new byte[70_000], PieceBytes = 64 * 1024, Secure = sent };

            Assert.Equal(200, (await file.BeginAsync(data, _path)).Status);
            Assert.Equal(kept, _store.Find("a.dmp").Key.Secure);
        }

        [Fact]
        public async Task ABeginWithTheOtherMark_IsAnotherUpload()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var secure = TestFile.Random("a.dmp", 200_000);
            var notSecure = new TestFile { Name = secure.Name, Bytes = secure.Bytes, PieceBytes = secure.PieceBytes, Secure = false };

            var first = await file.BeginAsync(secure, _path);
            await file.SendPieceAsync(secure.Piece(0), first.Upload);

            var second = await file.BeginAsync(notSecure, _path);

            Assert.Equal(0, second.Next);
            Assert.NotEqual(first.Upload, second.Upload);
        }

        // A routed ordinary send keeps its status and comment; the store gets who sent it from where.
        [Fact]
        public async Task TheStatusAndCommentOfARoutedSend_AndTheSessionsClient_ReachTheStore()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = new TestFile { Name = "report.bin", Bytes = new byte[70_000], PieceBytes = 64 * 1024, Secure = false, Status = 3, Comment = "disk is full" };

            Assert.Equal(200, (await file.BeginAsync(data, _path)).Status);

            var entry = _store.Find("report.bin");

            Assert.Equal(HSMSensorDataObjects.SensorStatus.Error, entry.Status);
            Assert.Equal("disk is full", entry.Comment);
            Assert.Equal(TableConnectionTestClient.ClientName, entry.ClientName);
            Assert.Equal("127.0.0.1", entry.RemoteAddress);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ABeginWithoutSecureStatusOrComment_OrWithNulls_IsSecureOkWithNoComment(bool nulls)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            var fields = nulls ? ",\"secure\":null,\"status\":null,\"comment\":null,\"source\":null" : string.Empty;
            await file.SendTextAsync($"{{\"id\":1,\"op\":\"begin\",\"body\":{{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536{fields}}}}}");

            Assert.Equal(200, (await file.ReceiveAckAsync()).Status);

            var entry = _store.Find("a");

            Assert.True(entry.Key.Secure);
            Assert.Equal(HSMSensorDataObjects.SensorStatus.Ok, entry.Status);
            Assert.Null(entry.Comment);
            Assert.Null(entry.Key.Source);
        }

        // After a 202 the connection no longer holds the upload: a 409 to a later end sends the collector to
        // begin, which answers the missing piece.
        [Fact]
        public async Task A409ToEndAfterA202_IsFollowedByBegin_WhichAnswersTheMissingPiece()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();
            _store.FinishFindsLastPieceGone = true;

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            Assert.Equal(202, (await file.EndAsync(begun.Upload, data.Hash())).Status);

            gate.Release.SetResult();

            var missing = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(409, missing.Status);
            Assert.Equal(data.PieceCount - 1, missing.Next);

            var again = await file.BeginAsync(data, _path);

            Assert.Equal(begun.Upload, again.Upload);
            Assert.Equal(data.PieceCount - 1, again.Next);

            await file.SendPieceAsync(data.Piece(data.PieceCount - 1), again.Upload);

            Assert.Equal("stored", (await file.EndUntilSettledAsync(again.Upload, data.Hash())).State);
        }

        [Fact]
        public async Task AnEmptyFile_HasNoPieces_AndIsStoredAtItsEnd()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = new TestFile { Name = "empty.dmp", Bytes = [], PieceBytes = 64 * 1024 };

            var begun = await file.BeginAsync(data, _path);

            Assert.Equal(0, begun.Next);
            Assert.Equal(0, begun.Pieces);
            Assert.Equal(Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant(), data.Hash());

            Assert.Equal("stored", (await file.EndUntilSettledAsync(begun.Upload, data.Hash())).State);
            Assert.Equal(SHA256.HashData([]), _store.Find("empty.dmp").ContentHash);
        }

        [Fact]
        public async Task ARepeatedPiece_IsStoredOnce_AndAnsweredWithTheStoresNext()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);
            Assert.Equal(2, (await file.SendPieceAsync(data.Piece(1), begun.Upload)).Next);

            var repeat = await file.SendPieceAsync(data.Piece(0), begun.Upload);

            Assert.Equal(200, repeat.Status);
            Assert.Equal(2, repeat.Next);
            Assert.Equal(2, _store.Find("a.dmp").Pieces.Count);
        }

        [Fact]
        public async Task APieceAheadOfTheNext_Is409WithTheNext_AndNothingIsWritten()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            var ahead = await file.SendPieceAsync(data.Piece(2), begun.Upload);

            Assert.Equal(409, ahead.Status);
            Assert.Equal(0, ahead.Next);
            Assert.Equal("out of order", ahead.Reason);
            Assert.Empty(_store.Find("a.dmp").Pieces);

            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);
        }

        [Fact]
        public async Task EndWithPiecesMissing_Is409WithTheNext_AndTheUploadGoesOn()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            var early = await file.EndAsync(begun.Upload, data.Hash());

            Assert.Equal(409, early.Status);
            Assert.Equal(1, early.Next);

            for (var i = 1; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            Assert.Equal("stored", (await file.EndUntilSettledAsync(begun.Upload, data.Hash())).State);
        }

        // A second begin on a connection that holds an upload gives the first back.
        [Fact]
        public async Task ASecondBegin_GivesTheFirstUploadBack()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var first = TestFile.Random("a.dmp", 200_000);
            var second = TestFile.Random("b.dmp", 200_000, seed: 2);

            var a = await file.BeginAsync(first, _path);
            await file.SendPieceAsync(first.Piece(0), a.Upload);

            var b = await file.BeginAsync(second, _path);

            Assert.Equal(200, b.Status);
            Assert.Equal(1, _store.LiveLeases);

            // The first upload's pieces now get 404: this connection no longer holds it.
            Assert.Equal(404, (await file.SendPieceAsync(first.Piece(1), a.Upload)).Status);
        }

        // Begin with another piece size than the part was begun with: the upload starts again at 0.
        [Fact]
        public async Task ABeginWithAnotherPieceSize_StartsTheUploadAgain()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 400_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            var smaller = new TestFile { Name = data.Name, Bytes = data.Bytes, PieceBytes = 128 * 1024 };
            var again = await file.BeginAsync(smaller, _path);

            Assert.Equal(200, again.Status);
            Assert.Equal(0, again.Next);
            Assert.Equal(128 * 1024, again.PieceBytes);
            Assert.NotEqual(begun.Upload, again.Upload);
        }


        // === A cut and a continue ===

        // A cut at every point of an upload, and a begin on a new connection that goes on from the store's
        // number: after the begin, after each piece, in the middle of a piece, after the end.
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(1, true)]
        public async Task ACut_AndAContinueOnANewConnection_StoresTheFileWhole(int piecesBeforeTheCut, bool midPiece)
        {
            await using var host = await StartHost();
            var data = TestFile.Random("a.dmp", 3 * 64 * 1024 + 5, seed: 3);
            string upload;

            using (var first = await OpenFileAsync(host))
            {
                var begun = await first.BeginAsync(data, _path);
                upload = begun.Upload;

                for (var i = 0; i < piecesBeforeTheCut; i++)
                    await first.SendPieceAsync(data.Piece(i), upload);

                if (midPiece)
                {
                    var frame = data.Piece(piecesBeforeTheCut).Frame(99, upload);
                    await first.Socket.SendAsync(frame.AsMemory(0, frame.Length / 2), WebSocketMessageType.Binary, false, Deadline());
                }

                first.Socket.Abort();
            }

            await TestWait.UntilAsync(() => host.Registry.FileConnectionCount == 0 && _store.LiveLeases == 0, "the cut connection to give its upload back");
            Assert.Equal(piecesBeforeTheCut, _store.Find("a.dmp")?.Pieces.Count ?? 0);

            using var second = await OpenFileAsync(host);

            var stored = await second.UploadAsync(data, _path);

            Assert.Equal("stored", stored.State);
            Assert.Equal(SHA256.HashData(data.Bytes), _store.Find("a.dmp").ContentHash);

            if (piecesBeforeTheCut > 0)
                Assert.Equal(upload, (await second.BeginAsync(data, _path)).Upload);
        }

        // A cut while the end check runs: the check goes on; a begin on a new connection says checking,
        // then stored.
        [Fact]
        public async Task ACutDuringTheCheck_LeavesTheCheckRunning_AndBeginTellsItsResult()
        {
            await using var host = await StartHost();
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            using (var first = await OpenFileAsync(host))
            {
                var begun = await first.BeginAsync(data, _path);

                for (var i = 0; i < data.PieceCount; i++)
                    await first.SendPieceAsync(data.Piece(i), begun.Upload);

                Assert.Equal(202, (await first.EndAsync(begun.Upload, data.Hash())).Status);
                await gate.Entered.Task.WaitAsync(Patience);

                first.Socket.Abort();
            }

            await TestWait.UntilAsync(() => host.Registry.FileConnectionCount == 0, "the cut connection to end");
            Assert.Equal(1, _store.LiveLeases);

            using var second = await OpenFileAsync(host);

            var checking = await second.BeginAsync(data, _path);

            Assert.Equal(202, checking.Status);
            Assert.Equal("checking", checking.State);
            Assert.Equal(5, checking.RetryAfter);

            gate.Release.SetResult();
            await TestWait.UntilAsync(() => host.Handler.FileChecksRunning == 0 && _store.LiveLeases == 0, "the check to end and give the lease back");

            Assert.Equal("stored", (await second.BeginAsync(data, _path)).State);
        }

        // A restart of the server: a new host over the same store.
        [Fact]
        public async Task AContinueAfterARestartOfTheServer_GoesOnFromTheStoresNumber()
        {
            var data = TestFile.Random("a.dmp", 200_000);

            await using (var before = await StartHost())
            {
                using var file = await OpenFileAsync(before);

                var begun = await file.BeginAsync(data, _path);
                await file.SendPieceAsync(data.Piece(0), begun.Upload);
                await file.SendPieceAsync(data.Piece(1), begun.Upload);
            }

            await using var after = await StartHost();
            using var again = await OpenFileAsync(after);

            Assert.Equal(2, (await again.BeginAsync(data, _path)).Next);
            Assert.Equal("stored", (await again.UploadAsync(data, _path)).State);
        }


        // === Pieces that break the rules ===

        [Fact]
        public async Task AWrongChecksum_RefusesThePiece_AndKeepsTheUpload()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            var bad = data.Piece(0);
            bad.Checksum = 12345;

            var refused = await file.SendPieceAsync(bad, begun.Upload);

            Assert.Equal(409, refused.Status);
            Assert.Equal(0, refused.Next);
            Assert.Equal("checksum mismatch", refused.Reason);
            Assert.Equal(0, _store.Appends);

            Assert.Equal("stored", (await file.UploadAsync(data, _path)).State);
        }

        public static TheoryData<string> SizeViolations => new() { "decompressed length", "compressed length", "piece number", "content size", "two frames", "no content size" };

        // A piece that breaks a rule of its size discards the upload: 400, and the part is gone.
        [Theory]
        [MemberData(nameof(SizeViolations))]
        public async Task APieceThatBreaksTheSizeRules_Is400_AndDiscardsTheUpload(string violation)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            var piece = data.Piece(1);

            switch (violation)
            {
                case "decompressed length":
                    piece.DecompressedLength = (uint)piece.Original.Length - 1;
                    break;
                case "compressed length":
                    piece.CompressedLength = (uint)piece.Compressed.Length + 1;
                    break;
                case "piece number":
                    piece = TestPiece.Of(data.PieceCount, piece.Original);
                    break;
                case "content size":
                    // A frame of fewer bytes than the header says the piece has.
                    piece.Compressed = TestPiece.Of(1, piece.Original[..^1]).Compressed;
                    break;
                case "two frames":
                    piece.Compressed = [.. piece.Compressed, .. TestPiece.Of(1, [1, 2, 3]).Compressed];
                    break;
                case "no content size":
                    piece.Compressed = StreamedFrame(piece.Original);
                    break;
            }

            var refused = await file.SendPieceAsync(piece, begun.Upload);

            Assert.Equal(400, refused.Status);
            Assert.EndsWith("the upload is discarded", refused.Error);
            Assert.Null(_store.Find("a.dmp"));
            Assert.Equal(0, _store.LiveLeases);

            // The connection holds no upload any more: an end of it is 404, and a piece, larger than 64 KiB,
            // is not let in.
            Assert.Equal(404, (await file.EndAsync(begun.Upload, data.Hash())).Status);

            await file.SendBinaryAsync(data.Piece(1).Frame(77, begun.Upload));
            Assert.Equal(WebSocketCloseStatus.ProtocolError, await file.ExpectCloseAsync());
        }

        // === discard (ADR-0022 point 3: the source file changed while it was sent) ===

        [Fact]
        public async Task ADiscard_RemovesTheUploadThisConnectionHolds_AndTheFileBeginsAgainAtZero()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            var discarded = await file.DiscardAsync(begun.Upload);

            Assert.Equal(200, discarded.Status);
            Assert.Equal("discarded", discarded.State);
            Assert.Equal(begun.Upload, discarded.Upload);
            Assert.Null(_store.Find("a.dmp"));
            Assert.Equal(0, _store.LiveLeases);
            Assert.Single(host.Log.Matching($"upload {begun.Upload}", "is discarded: the collector discarded it"));

            // The connection holds no upload: a second discard is 404, and the file begins again from 0.
            Assert.Equal(404, (await file.DiscardAsync(begun.Upload)).Status);

            var again = await file.BeginAsync(data, _path);

            Assert.Equal(0, again.Next);
            Assert.NotEqual(begun.Upload, again.Upload);
        }

        [Fact]
        public async Task ADiscardOfAnUploadThisConnectionDoesNotHold_Is404_AndAMalformedOne400()
        {
            await using var host = await StartHost();
            using var other = await OpenFileAsync(host);
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await other.BeginAsync(data, _path);

            Assert.Equal(404, (await file.DiscardAsync(begun.Upload)).Status);
            Assert.NotNull(_store.Find("a.dmp"));

            await file.SendTextAsync("{\"id\":5,\"op\":\"discard\",\"body\":{\"upload\":\"xyz\"}}");
            Assert.Equal(400, (await file.ReceiveAckAsync()).Status);
        }

        [Fact]
        public async Task ADiscardWhileItsEndCheckRuns_Is409Checking()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            Assert.Equal(202, (await file.EndAsync(begun.Upload, data.Hash())).Status);
            await gate.Entered.Task.WaitAsync(Patience);

            var refused = await file.DiscardAsync(begun.Upload);

            Assert.Equal(409, refused.Status);
            Assert.Equal("checking", refused.State);
            Assert.Equal(1, refused.RetryAfter);

            gate.Release.SetResult();
            Assert.Equal("stored", (await file.EndUntilSettledAsync(begun.Upload, data.Hash())).State);
        }

        [Fact]
        public async Task ADiscardTheStoreCannotMake_Is503_AndTheUploadStaysHeldForAnother()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            _store.DiscardFaults = true;
            var fault = await file.DiscardAsync(begun.Upload);

            Assert.Equal(503, fault.Status);
            Assert.Equal(60, fault.RetryAfter);
            Assert.Equal(1, _store.LiveLeases);
            Assert.Single(_store.Find("a.dmp").Pieces);

            _store.DiscardFaults = false;
            var entry = _store.Find("a.dmp");

            Assert.Equal("discarded", (await file.DiscardAsync(begun.Upload)).State);
            Assert.Null(_store.Find("a.dmp"));
            Assert.Equal("the collector discarded it: its source file changed", entry.DiscardReason);
        }

        // A discard with a stale lease (another connection holds the upload now) removes nothing and says so.
        [Fact]
        public async Task ADiscardWithAStaleLease_Is404_AndRemovesNothing()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            _store.TakeLease("a.dmp");

            Assert.Equal(404, (await file.DiscardAsync(begun.Upload)).Status);
            Assert.Single(_store.Find("a.dmp").Pieces);
        }

        // An append answered with a lost lease gives the lease back at once.
        [Fact]
        public async Task APieceAnsweredLeaseLost_ReleasesTheLease()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            _store.TakeLease("a.dmp");
            var releases = _store.Releases;

            Assert.Equal(404, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Status);
            Assert.Equal(releases + 1, _store.Releases);
        }

        // The memory bound of file frames holds after a lease expires: a connection that sent nothing for the store's
        // lease timeout no longer holds its upload, and a frame above 64 KiB closes it with 1002.
        [Fact]
        public async Task AnUploadWhoseLeaseExpired_IsForgotten_AndALargeFrameCloses1002()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);

            host.Time.Advance(_store.LeaseTimeout);

            await file.SendBinaryAsync(data.Piece(1).Frame(9, begun.Upload));

            Assert.Equal(WebSocketCloseStatus.ProtocolError, await file.ExpectCloseAsync());
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the expired lease to be given back");
        }

        [Fact]
        public async Task AnUploadWhoseLeaseIsAlive_KeepsTakingPieces()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            host.Time.Advance(_store.LeaseTimeout - TimeSpan.FromSeconds(5));
            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);

            // A stored piece renews it.
            host.Time.Advance(_store.LeaseTimeout - TimeSpan.FromSeconds(5));
            Assert.Equal(2, (await file.SendPieceAsync(data.Piece(1), begun.Upload)).Next);
        }

        // A discard that does not complete (cut short, faulted) still gives the lease back: a lease left
        // behind would hold an upload slot and make the file busy until the store's lease timeout.
        [Fact]
        public async Task ADiscardThatDoesNotComplete_StillGivesTheLeaseBack()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            var piece = data.Piece(0);
            piece.DecompressedLength = 1;

            _store.DiscardThrows = true;

            Assert.Equal(400, (await file.SendPieceAsync(piece, begun.Upload)).Status);
            Assert.Equal(0, _store.LiveLeases);
        }

        // Stated content size, one frame, no window larger than the piece: everything the arrival can see.
        // A frame whose blocks hold fewer bytes than it states is found only by decompressing it, in the end
        // check: 400, and the upload is discarded.
        [Fact]
        public async Task APieceThatDecompressesToTheWrongSize_IsFoundByTheEndCheck_AndDiscardsTheUpload()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 64 * 1024);

            var begun = await file.BeginAsync(data, _path);
            var piece = data.Piece(0);
            piece.Compressed = RawFrameShortOfItsContentSize(piece.Original);

            Assert.Equal(1, (await file.SendPieceAsync(piece, begun.Upload)).Next);

            var answer = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(400, answer.Status);
            Assert.StartsWith("piece 0: the piece does not decompress", answer.Error);
            Assert.EndsWith("the upload is discarded", answer.Error);
            Assert.Null(_store.Find("a.dmp"));

            // Begin again: a new upload from piece 0.
            var again = await file.BeginAsync(data, _path);

            Assert.Equal(0, again.Next);
            Assert.NotEqual(begun.Upload, again.Upload);
        }

        [Fact]
        public async Task AWrongHash_DiscardsTheUpload_AndBeginStartsAgainAtZero()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            var answer = await file.EndUntilSettledAsync(begun.Upload, new string('0', 64));

            Assert.Equal(400, answer.Status);
            Assert.Equal("the hash of the whole file does not match: the upload is discarded", answer.Error);
            Assert.Null(_store.Find("a.dmp"));
            Assert.Single(host.Log.Matching($"upload {begun.Upload}", "is discarded, its end check failed"));

            Assert.Equal(0, (await file.BeginAsync(data, _path)).Next);
        }

        [Fact]
        public async Task APieceWhoseBytesDoNotMatchItsDigest_FailsTheEndCheckNamingThePiece()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
            {
                var piece = data.Piece(i);

                if (i == 2)
                    piece.Digest = new byte[32];

                await file.SendPieceAsync(piece, begun.Upload);
            }

            var answer = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(400, answer.Status);
            Assert.Equal("piece 2: its bytes do not match its digest: the upload is discarded", answer.Error);
        }

        [Fact]
        public async Task APieceOfAnotherUpload_Is404()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            var ack = await file.SendPieceAsync(data.Piece(0), Guid.NewGuid().ToString("N"));

            Assert.Equal(404, ack.Status);
            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);
        }

        [Fact]
        public async Task ALargeFrameBeforeAnAcceptedBegin_Closes1002()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            await file.SendBinaryAsync(data.Piece(0).Frame(1, Guid.NewGuid().ToString("N")));

            Assert.Equal(WebSocketCloseStatus.ProtocolError, await file.ExpectCloseAsync());
            Assert.Equal("a frame over 64 KiB arrived before an accepted begin", file.Socket.CloseStatusDescription);
        }

        [Theory]
        [InlineData("short")]
        [InlineData("version")]
        [InlineData("kind")]
        [InlineData("reserved")]
        [InlineData("header length")]
        public async Task AHeaderThatCannotBeTrusted_Closes1002(string defect)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            var frame = Probe(1);

            switch (defect)
            {
                case "short":
                    frame = frame[..87];
                    break;
                case "version":
                    frame[0] = 2;
                    break;
                case "kind":
                    frame[1] = 3;
                    break;
                case "reserved":
                    frame[53] = 1;
                    break;
                case "header length":
                    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 2000);
                    break;
            }

            await file.SendBinaryAsync(frame);

            Assert.Equal(WebSocketCloseStatus.ProtocolError, await file.ExpectCloseAsync());
        }


        // === Refusals of begin and end ===

        public static TheoryData<string, string> MalformedBegins => new()
        {
            { "{\"id\":1,\"op\":\"begin\"}", "malformed body" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536}}", "invalid begin: path must have 1..1024 characters" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"" + new string('p', 1025) + "\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536}}", "invalid begin: path must have 1..1024 characters" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536}}", "invalid begin: name must have 1..1024 characters" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":-1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536}}", "invalid begin: size must be a whole number of bytes, 0 or more" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00\",\"pieceBytes\":65536}}", "invalid begin: modified must be a time with a UTC designator or an offset" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"source\":\"" + new string('s', 257) + "\",\"pieceBytes\":65536}}", "invalid begin: source must have 1..256 characters" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65535}}", "invalid begin: pieceBytes must be 65536.." },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536,\"secure\":\"yes\"}}", "invalid begin: secure must be true or false" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536,\"status\":7}}", "invalid begin: status must be a SensorStatus number" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536,\"status\":\"Ok\"}}", "invalid begin: status must be a SensorStatus number" },
            { "{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T10:00:00Z\",\"pieceBytes\":65536,\"comment\":\"" + new string('c', 4097) + "\"}}", "invalid begin: comment must be text of at most 4096 characters" },
        };

        [Theory]
        [MemberData(nameof(MalformedBegins))]
        public async Task AMalformedBegin_Is400_BeforeTheStore(string frame, string error)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            await file.SendTextAsync(frame);
            var ack = await file.ReceiveAckAsync();

            Assert.Equal(400, ack.Status);
            Assert.StartsWith(error, ack.Error);
            Assert.Equal(0, _store.Count);
        }

        [Fact]
        public async Task ABeginWithAnOffset_IsTakenAsItsUtcTime()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            await file.SendTextAsync("{\"id\":1,\"op\":\"begin\",\"body\":{\"path\":\"p\",\"name\":\"a\",\"size\":1,\"modified\":\"2026-11-25T12:00:00+02:00\",\"source\":\"vol:1/file:2\",\"pieceBytes\":65536}}");

            Assert.Equal(200, (await file.ReceiveAckAsync()).Status);

            var key = _store.Find("a").Key;

            Assert.Equal(new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc), key.Modified);
            Assert.Equal("vol:1/file:2", key.Source);
        }

        [Fact]
        public async Task TheStoresRefusals_AreTheirStatuses()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 100_000);

            _store.MaxFileBytes = 99_999;
            var tooLarge = await file.BeginAsync(data, _path);
            Assert.Equal(413, tooLarge.Status);
            Assert.Null(tooLarge.RetryAfter);
            _store.MaxFileBytes = long.MaxValue;

            _store.RefusedPaths.Add(_path);
            var refused = await file.BeginAsync(data, _path);
            Assert.Equal(422, refused.Status);
            Assert.StartsWith("the sensor cannot take the file: No sensor is registered", refused.Error);
            _store.RefusedPaths.Clear();

            _store.NoRoom = true;
            var noRoom = await file.BeginAsync(data, _path);
            Assert.Equal(503, noRoom.Status);
            Assert.Equal(300, noRoom.RetryAfter);
            Assert.Equal("no room for the file on the server's disk", noRoom.Error);
            _store.NoRoom = false;

            var tooBig = await file.BeginAsync(data, _path, pieceBytes: host.Handler.FilePieceBytes + 4096);
            Assert.Equal(400, tooBig.Status);
        }

        // Not final: a product with as many unfinished uploads as the store keeps, and an outcome the
        // connection does not know, are 503 with a time, never a final refusal.
        [Theory]
        [InlineData(FileBeginOutcome.TooManyParts, "too many unfinished uploads of this product", 8)]
        [InlineData((FileBeginOutcome)99, "the large file store cannot take files now", 60)]
        public async Task ARefusalThatPasses_Is503WithATime(FileBeginOutcome outcome, string error, int retryAfter)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            _store.NextBeginOutcome = outcome;
            var ack = await file.BeginAsync(TestFile.Random("a.dmp", 100_000), _path);

            Assert.Equal(503, ack.Status);
            Assert.Equal(error, ack.Error);
            Assert.Equal(retryAfter, ack.RetryAfter);
        }

        [Fact]
        public async Task AnUnknownOp_Is400()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            await file.SendTextAsync("{\"id\":3,\"op\":\"list\",\"body\":[]}");
            var ack = await file.ReceiveAckAsync();

            Assert.Equal(400, ack.Status);
            Assert.Equal("unknown op", ack.Error);
        }

        [Theory]
        [InlineData("{\"id\":1,\"op\":\"end\",\"body\":{\"upload\":\"xyz\",\"hash\":\"00\"}}", 400)]
        [InlineData("{\"id\":1,\"op\":\"end\",\"body\":{\"upload\":\"0123456789abcdef0123456789abcdef\",\"hash\":\"0000000000000000000000000000000000000000000000000000000000000000\"}}", 404)]
        public async Task AnEndThatNamesNoUploadOfThisConnection_IsRefused(string frame, int status)
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);

            await file.SendTextAsync(frame);

            Assert.Equal(status, (await file.ReceiveAckAsync()).Status);
        }

        // One live upload per part: the same file on another connection is busy.
        [Fact]
        public async Task TheSameFileOnASecondConnection_IsBusy()
        {
            await using var host = await StartHost();
            using var first = await OpenFileAsync(host);
            using var second = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            Assert.Equal(200, (await first.BeginAsync(data, _path)).Status);

            var busy = await second.BeginAsync(data, _path);

            Assert.Equal(409, busy.Status);
            Assert.Equal(30, busy.RetryAfter);
        }

        // The bound on uploads in progress: what bounds the memory of file frames (no per-key rule, no 1013).
        [Fact]
        public async Task AnUploadBeyondTheBound_Is503_UntilOneEnds()
        {
            await using var host = await StartHost();
            _store.MaxUploadsInProgress = 1;

            using var first = await OpenFileAsync(host);
            using var second = await OpenFileAsync(host);
            var a = TestFile.Random("a.dmp", 100_000);
            var b = TestFile.Random("b.dmp", 100_000, seed: 2);

            var begun = await first.BeginAsync(a, _path);

            var refused = await second.BeginAsync(b, _path);

            Assert.Equal(503, refused.Status);
            Assert.Equal("too many uploads in progress", refused.Error);
            Assert.Equal(60, refused.RetryAfter);

            // A large frame of the refused connection is not let in: it holds no upload.
            await second.SendBinaryAsync(b.Piece(0).Frame(1, begun.Upload));
            Assert.Equal(WebSocketCloseStatus.ProtocolError, await second.ExpectCloseAsync());

            for (var i = 0; i < a.PieceCount; i++)
                await first.SendPieceAsync(a.Piece(i), begun.Upload);

            Assert.Equal("stored", (await first.EndUntilSettledAsync(begun.Upload, a.Hash())).State);
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the check to give the lease back");

            using var third = await OpenFileAsync(host);
            Assert.Equal(200, (await third.BeginAsync(b, _path)).Status);
        }


        // === Faults ===

        [Fact]
        public async Task AWriteThatFails_Is503WithATime_AndThePieceIsSentAgain()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            _store.FaultWrites = true;
            var fault = await file.SendPieceAsync(data.Piece(0), begun.Upload);

            Assert.Equal(503, fault.Status);
            Assert.Equal(60, fault.RetryAfter);
            Assert.Empty(_store.Find("a.dmp").Pieces);

            _store.FaultWrites = false;
            Assert.Equal(1, (await file.SendPieceAsync(data.Piece(0), begun.Upload)).Next);
        }

        [Fact]
        public async Task ACheckThatCannotRead_Is503_AndTheFileIsKeptForAnotherEnd()
        {
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            _store.FaultChecks = true;
            var fault = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(503, fault.Status);
            Assert.Equal(60, fault.RetryAfter);
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the check to give the lease back");

            _store.FaultChecks = false;

            var again = await file.BeginAsync(data, _path);

            Assert.Equal(data.PieceCount, again.Next);
            Assert.Equal("stored", (await file.EndUntilSettledAsync(again.Upload, data.Hash())).State);
        }


        // === Access and shutdown ===

        [Fact]
        public async Task LostAccessInTheMiddle_Closes1008_AndThePartStays()
        {
            await using var host = await StartHost();
            var key = AddKey();
            using var file = await OpenFileAsync(host, key.ToString());
            var data = TestFile.Random("a.dmp", 200_000);

            var begun = await file.BeginAsync(data, _path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            Block(key);

            await file.SendBinaryAsync(data.Piece(1).Frame(5, begun.Upload));

            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await file.ExpectCloseAsync());
            Assert.Single(_store.Find("a.dmp").Pieces);
            await TestWait.UntilAsync(() => _store.LiveLeases == 0, "the lease to go back");
        }

        // A check goes on after the key lost access: the file was sent whole while the key could send.
        [Fact]
        public async Task ACheckGoesOn_AfterTheKeyLostAccess()
        {
            await using var host = await StartHost();
            var key = AddKey();
            using var file = await OpenFileAsync(host, key.ToString());
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            var begun = await file.BeginAsync(data, _path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            Assert.Equal(202, (await file.EndAsync(begun.Upload, data.Hash())).Status);
            await gate.Entered.Task.WaitAsync(Patience);

            Block(key);
            await file.SendTextAsync("{\"op\":\"ping\"}");
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await file.ExpectCloseAsync());

            gate.Release.SetResult();

            await TestWait.UntilAsync(() => _store.Find("a.dmp").State == FakeFileUploadStore.State.Verified, "the check to store the file");
        }

        [Fact]
        public async Task ShutdownInTheMiddleOfAPiece_Closes1001_AndStoresNothingOfIt()
        {
            var host = await StartHost();
            var data = TestFile.Random("a.dmp", 200_000);

            try
            {
                using var file = await OpenFileAsync(host);

                var begun = await file.BeginAsync(data, _path);
                await file.SendPieceAsync(data.Piece(0), begun.Upload);

                var frame = data.Piece(1).Frame(9, begun.Upload);
                await file.Socket.SendAsync(frame.AsMemory(0, frame.Length / 2), WebSocketMessageType.Binary, false, Deadline());

                var stop = Task.Run(host.StopAsync);

                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await file.ExpectCloseAsync());
                await stop.WaitAsync(Patience);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.Single(_store.Find("a.dmp").Pieces);
            Assert.Equal(0, _store.LiveLeases);
        }

        // Shutdown while a piece is being written: the piece is acknowledged before the close.
        [Fact]
        public async Task ShutdownDuringAWrite_AcknowledgesThePieceBeforeClosing()
        {
            var host = await StartHost();
            var data = TestFile.Random("a.dmp", 200_000);

            try
            {
                using var file = await OpenFileAsync(host);

                var begun = await file.BeginAsync(data, _path);
                var gate = _store.AppendGate = new FakeFileUploadStore.Gate();

                await file.SendBinaryAsync(data.Piece(0).Frame(3, begun.Upload));
                await gate.Entered.Task.WaitAsync(Patience);

                var stop = Task.Run(host.StopAsync);

                await TestWait.UntilAsync(() => host.Registry.AnyDeferredClose, "the shutdown to ask the busy file connection to close");
                gate.Release.SetResult();

                Assert.Equal(1, (await file.ReceiveAckAsync()).Next);
                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await file.ExpectCloseAsync());

                await stop.WaitAsync(Patience);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.Single(_store.Find("a.dmp").Pieces);
        }

        // Shutdown while an end waits for its check: the wait ends with the start of the shutdown, the end is
        // answered 202 and the connection closed with 1001. (The wait is made a minute long: without the end of
        // the wait the answer would not come within the test's patience.)
        [Fact]
        public async Task ShutdownWhileAnEndWaitsForItsCheck_EndsTheWait()
        {
            var host = await StartHost();
            host.Handler.FileQuickCheck = TimeSpan.FromMinutes(1);
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            try
            {
                using var file = await OpenFileAsync(host);

                var begun = await file.BeginAsync(data, _path);

                for (var i = 0; i < data.PieceCount; i++)
                    await file.SendPieceAsync(data.Piece(i), begun.Upload);

                await file.SendTextAsync(TestFile.End(99, begun.Upload, data.Hash()));
                await gate.Entered.Task.WaitAsync(Patience);

                var stop = Task.Run(host.StopAsync);

                Assert.Equal(202, (await file.ReceiveAckAsync()).Status);
                Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await file.ExpectCloseAsync());

                await stop.WaitAsync(Patience);
            }
            finally
            {
                await host.DisposeAsync();
            }
        }

        // Shutdown during a check: the check is cancelled and awaited; nothing changes, and after the restart
        // the collector ends the upload again.
        [Fact]
        public async Task ShutdownDuringACheck_CancelsAndAwaitsIt_AndTheUploadEndsAgainAfterTheRestart()
        {
            var host = await StartHost();
            var data = TestFile.Random("a.dmp", 100_000);
            var gate = _store.FinishGate = new FakeFileUploadStore.Gate();

            try
            {
                using var file = await OpenFileAsync(host);

                var begun = await file.BeginAsync(data, _path);

                for (var i = 0; i < data.PieceCount; i++)
                    await file.SendPieceAsync(data.Piece(i), begun.Upload);

                Assert.Equal(202, (await file.EndAsync(begun.Upload, data.Hash())).Status);
                await gate.Entered.Task.WaitAsync(Patience);

                await host.StopAsync().WaitAsync(Patience);

                Assert.Equal(0, host.Handler.FileChecksRunning);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.Equal(FakeFileUploadStore.State.Uploading, _store.Find("a.dmp").State);
            Assert.Equal(0, _store.LiveLeases);

            await using var after = await StartHost();
            using var again = await OpenFileAsync(after);

            var begunAgain = await again.BeginAsync(data, _path);

            Assert.Equal(data.PieceCount, begunAgain.Next);
            Assert.Equal("stored", (await again.EndUntilSettledAsync(begunAgain.Upload, data.Hash())).State);
        }


        // === Helpers ===

        private Task<SensorApiTestHost> StartHost(Action<CollectorSessionsConfig> sessions = null)
        {
            var config = new CollectorSessionsConfig();
            sessions?.Invoke(config);

            return SensorApiTestHost.StartAsync(_valuesCache, config, files: _store);
        }

        private Task<FileConnectionTestClient> OpenFileAsync(SensorApiTestHost host) => OpenFileAsync(host, _key);

        // A session of its own and its file connection; the client keeps the session's socket.
        private static async Task<FileConnectionTestClient> OpenFileAsync(SensorApiTestHost host, string key)
        {
            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, key);

            var file = await FileConnectionTestClient.OpenAsync(host.SessionUri, key, sessionId);

            file.Session = session;
            return file;
        }

        private static byte[] Probe(long id)
        {
            var frame = new byte[88];

            frame[0] = 1;
            frame[1] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 88);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), id);

            return frame;
        }

        // A zstd frame written by streaming, which does not state its content size.
        private static byte[] StreamedFrame(byte[] original)
        {
            using var output = new System.IO.MemoryStream();

            using (var stream = new ZstdSharp.CompressionStream(output, 3, leaveOpen: true))
                stream.Write(original);

            return output.ToArray();
        }

        // A zstd frame by the format (RFC 8878) that states the content size of `original` in a single
        // segment and holds one raw block of one byte fewer: everything checked at arrival holds, and it
        // decompresses to the wrong size.
        internal static byte[] RawFrameShortOfItsContentSize(byte[] original)
        {
            Assert.InRange(original.Length, 257, 65_791);

            var block = original.Length - 1;
            var frame = new byte[4 + 1 + 2 + 3 + block];

            BinaryPrimitives.WriteUInt32LittleEndian(frame, 0xFD2FB528);
            frame[4] = 0x60; // Frame_Content_Size_flag 1 (2 bytes), Single_Segment_flag 1
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), (ushort)(original.Length - 256));

            var header = (block << 3) | 1; // Last_Block, Raw_Block
            frame[7] = (byte)header;
            frame[8] = (byte)(header >> 8);
            frame[9] = (byte)(header >> 16);

            original.AsSpan(0, block).CopyTo(frame.AsSpan(10));

            return frame;
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

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;
    }
}
