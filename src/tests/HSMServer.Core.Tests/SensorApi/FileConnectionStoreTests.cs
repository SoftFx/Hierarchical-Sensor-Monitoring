using HSMCommon.Model;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.SensorApi.Sessions.Files;
using HSMServer.ServerConfiguration;
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class FileConnectionStoreFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(FileConnectionStoreTests);
    }


    // The file connection (#1884) over the real store of large files (#1883): LargeFileUploadStore over the real
    // LargeFileUploadService, the real database and cache. FileConnectionTests run the whole protocol against a fake;
    // these run its main paths against what production runs: a file stored as the File sensor's value with the hash of
    // its original bytes, its mark, status, comment and origin in the record and the value, a continue on a new
    // connection and after a restart of the host, a wrong hash and a bad piece found by the end check, a discard, the
    // other mark as another upload, an unknown sensor.
    [Collection("Database collection")]
    public class FileConnectionStoreTests : MonitoringCoreTestsBase<FileConnectionStoreFixture>
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _root = $"fs{Guid.NewGuid():N}"[..12];
        private readonly LargeFileUploadService _uploads;
        private readonly GatedUploads _gated;
        private readonly LargeFileUploadStore _store;


        public FileConnectionStoreTests(FileConnectionStoreFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _uploads = new LargeFileUploadService(_databaseCoreManager.DatabaseCore, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
            _gated = new GatedUploads(_uploads);
            _store = new LargeFileUploadStore(_gated, LargeFileStoreLimits.DefaultMaxFileSizeBytes);
        }


        private ILargeFileStore Files => _databaseCoreManager.DatabaseCore.LargeFiles;


        [Fact]
        public async Task AFile_IsStoredAsTheSensorsValue_WithItsHashMarkStatusCommentAndOrigin()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = Routed(TestFile.Random("report.bin", 3 * 64 * 1024 + 77, seed: 4));

            var stored = await file.UploadAsync(data, sensor.Path);

            Assert.Equal(200, stored.Status);
            Assert.Equal("stored", stored.State);

            var record = Record(stored.Upload);

            Assert.Equal(LargeFileState.Verified, record.State);
            Assert.Equal(SHA256.HashData(data.Bytes), record.Hash);
            Assert.False(record.Secure);
            Assert.Equal(SensorStatus.Error, record.Status);
            Assert.Equal("disk is full", record.Comment);
            Assert.Equal(TableConnectionTestClient.ClientName, record.ClientName);
            Assert.Equal("127.0.0.1", record.RemoteAddress);
            Assert.Equal(sensor.Id, record.SensorId);

            var value = Assert.IsType<LargeFileValue>(sensor.LastValue);

            Assert.Equal(record.FileId, value.FileId);
            Assert.Equal(SHA256.HashData(data.Bytes), value.Hash);
            Assert.Equal(SensorStatus.Error, value.Status);
            Assert.Equal("disk is full", value.Comment);
            Assert.Equal(DateTime.Parse((string)stored.Body["time"]).ToUniversalTime(), value.Time);

            // A begin of the same file says it is stored.
            Assert.Equal("stored", (await file.BeginAsync(data, sensor.Path)).State);
        }

        [Fact]
        // The store itself is not restarted here (its reload from the disk is #1883's test): the web host is, with its
        // sessions and connections.
        public async Task ACutAndAContinueOnANewConnection_AndAfterARestartOfTheWebHost_StoreTheFileWhole()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            var data = TestFile.Random("app.dmp", 4 * 64 * 1024 + 5, seed: 5);
            string upload;

            await using (var before = await StartHost())
            {
                using var first = await OpenFileAsync(before);
                var begun = await first.BeginAsync(data, sensor.Path);
                upload = begun.Upload;

                await first.SendPieceAsync(data.Piece(0), upload);
                first.Socket.Abort();

                await TestWait.UntilAsync(() => before.Registry.FileConnectionCount == 0, "the cut connection to end");

                using var second = await OpenFileAsync(before);
                var again = await second.BeginAsync(data, sensor.Path);

                Assert.Equal(upload, again.Upload);
                Assert.Equal(1, again.Next);
                await second.SendPieceAsync(data.Piece(1), upload);
            }

            await using var after = await StartHost();
            using var third = await OpenFileAsync(after);

            var continued = await third.BeginAsync(data, sensor.Path);

            Assert.Equal(upload, continued.Upload);
            Assert.Equal(2, continued.Next);
            Assert.True(Record(upload).Secure);

            var stored = await third.UploadAsync(data, sensor.Path);

            Assert.Equal("stored", stored.State);
            Assert.Equal(SHA256.HashData(data.Bytes), Record(upload).Hash);
        }

        [Fact]
        public async Task AWrongHash_DiscardsThePart_AndBeginStartsANewUpload()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 6);

            var begun = await file.BeginAsync(data, sensor.Path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            var answer = await file.EndUntilSettledAsync(begun.Upload, new string('0', 64));

            Assert.Equal(400, answer.Status);
            Assert.Equal("the hash of the whole file does not match: the upload is discarded", answer.Error);
            Assert.Null(Record(begun.Upload));
            Assert.IsNotType<LargeFileValue>(sensor.LastValue);

            var again = await file.BeginAsync(data, sensor.Path);

            Assert.Equal(0, again.Next);
            Assert.NotEqual(begun.Upload, again.Upload);
        }

        [Fact]
        public async Task APieceWhoseBytesDoNotMatchItsDigest_IsNamedByTheEndCheck()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 3 * 64 * 1024, seed: 7);

            var begun = await file.BeginAsync(data, sensor.Path);

            for (var i = 0; i < data.PieceCount; i++)
            {
                var piece = data.Piece(i);

                if (i == 1)
                    piece.Digest = new byte[32];

                await file.SendPieceAsync(piece, begun.Upload);
            }

            var answer = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(400, answer.Status);
            Assert.Equal("piece 1: its bytes do not match its digest: the upload is discarded", answer.Error);
            Assert.Null(Record(begun.Upload));
        }

        [Fact]
        public async Task ADiscard_RemovesThePartFromTheStore()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 8);

            var begun = await file.BeginAsync(data, sensor.Path);
            await file.SendPieceAsync(data.Piece(0), begun.Upload);

            Assert.NotNull(Record(begun.Upload));
            Assert.Equal("discarded", (await file.DiscardAsync(begun.Upload)).State);
            Assert.Null(Record(begun.Upload));
        }

        [Fact]
        public async Task TheOtherMark_IsAnotherUpload_AndAnUnknownSensorIs422()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var secure = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 9);
            var notSecure = new TestFile { Name = secure.Name, Bytes = secure.Bytes, PieceBytes = secure.PieceBytes, Secure = false };

            var first = await file.BeginAsync(secure, sensor.Path);
            await file.SendPieceAsync(secure.Piece(0), first.Upload);

            var second = await file.BeginAsync(notSecure, sensor.Path);

            Assert.Equal(0, second.Next);
            Assert.NotEqual(first.Upload, second.Upload);
            Assert.True(Record(first.Upload).Secure);
            Assert.False(Record(second.Upload).Secure);

            var unknown = await file.BeginAsync(secure, $"{_root}/nothing");

            Assert.Equal(422, unknown.Status);
            Assert.StartsWith("the sensor cannot take the file: ", unknown.Error);
        }


        // A part that cannot give back what was stored (#1884 review): a defect of the content found by the end check, not
        // a fault answered 503 for days. The upload is discarded and the end answered 400 naming the piece.
        [Fact]
        public async Task AShortPart_IsADefectOfTheLastPiece_AndTheUploadIsDiscarded()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 3 * 64 * 1024, seed: 10);

            var begun = await file.BeginAsync(data, sensor.Path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            var record = Record(begun.Upload);
            var part = System.IO.Path.Combine(Files.Folder, "uploads", record.ProductId.ToString("N"), $"{record.FileId:N}.part");

            using (var stream = new System.IO.FileStream(part, System.IO.FileMode.Open))
                stream.SetLength(stream.Length - 1);

            var answer = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(400, answer.Status);
            Assert.StartsWith($"piece {data.PieceCount - 1}: the stored piece cannot be read back", answer.Error);
            Assert.EndsWith("the upload is discarded", answer.Error);
            Assert.Null(Record(begun.Upload));
            Assert.False(System.IO.File.Exists(part));
        }

        // A shutdown during a check: the store's own end of the finish clears the check, so after the restart a begin
        // continues (next = pieces) and the end runs the check again.
        [Fact]
        public async Task AShutdownDuringACheck_LeavesTheUploadToContinue()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            var data = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 11);
            var gate = _gated.HoldNextCheck();
            var host = await StartHost();
            string upload;

            try
            {
                using var file = await OpenFileAsync(host);
                var begun = await file.BeginAsync(data, sensor.Path);
                upload = begun.Upload;

                for (var i = 0; i < data.PieceCount; i++)
                    await file.SendPieceAsync(data.Piece(i), upload);

                Assert.Equal(202, (await file.EndAsync(upload, data.Hash())).Status);
                await gate.Entered.Task.WaitAsync(Patience);
                Assert.True(Files.IsChecking(Record(upload).FileId));

                await host.StopAsync().WaitAsync(Patience);
            }
            finally
            {
                await host.DisposeAsync();
            }

            Assert.False(Files.IsChecking(Record(upload).FileId));
            Assert.Equal(LargeFileState.Uploading, Record(upload).State);

            await using var after = await StartHost();
            using var again = await OpenFileAsync(after);

            var continued = await again.BeginAsync(data, sensor.Path);

            Assert.Equal(upload, continued.Upload);
            Assert.Equal(data.PieceCount, continued.Next);
            Assert.Equal("stored", (await again.EndUntilSettledAsync(upload, data.Hash())).State);
        }

        // A 409 to an end after its 202 sends the collector to begin, which the real store answers with its own next. The
        // 409 itself is forced (the store answers Incomplete only for pieces the connection already knows are missing).
        [Fact]
        public async Task A409ToEndAfterA202_IsFollowedByBegin_WhichTheStoreAnswers()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var file = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 12);
            var gate = _gated.HoldNextCheck(answerIncomplete: true);

            var begun = await file.BeginAsync(data, sensor.Path);

            for (var i = 0; i < data.PieceCount; i++)
                await file.SendPieceAsync(data.Piece(i), begun.Upload);

            Assert.Equal(202, (await file.EndAsync(begun.Upload, data.Hash())).Status);
            await gate.Entered.Task.WaitAsync(Patience);
            gate.Release.SetResult();

            var missing = await file.EndUntilSettledAsync(begun.Upload, data.Hash());

            Assert.Equal(409, missing.Status);

            var again = await file.BeginAsync(data, sensor.Path);

            Assert.Equal(begun.Upload, again.Upload);
            Assert.Equal(data.PieceCount, again.Next);
            Assert.Equal("stored", (await file.EndUntilSettledAsync(again.Upload, data.Hash())).State);
        }

        // Busy, with the rest of the lease as the time to wait.
        [Fact]
        public async Task ASecondConnectionWhileTheFirstHoldsTheLease_IsBusyForTheRestOfTheLease()
        {
            var sensor = await CreateFileSensorAsync("dumps");
            await using var host = await StartHost();
            using var first = await OpenFileAsync(host);
            using var second = await OpenFileAsync(host);
            var data = TestFile.Random("app.dmp", 2 * 64 * 1024, seed: 13);

            Assert.Equal(200, (await first.BeginAsync(data, sensor.Path)).Status);

            var busy = await second.BeginAsync(data, sensor.Path);

            Assert.Equal(409, busy.Status);
            Assert.InRange(busy.RetryAfter ?? 0, 170, LargeFileStoreLimits.DefaultLeaseTimeoutSeconds);
        }

        // A lease shorter than a frame's deadline (the silence limit) would let a frame outlast it: it is raised at start.
        [Theory]
        [InlineData(10, 30, 90)]
        [InlineData(180, 30, 180)]
        [InlineData(100, 40, 120)]
        public void TheLease_IsNeverShorterThanTheSilenceLimit(int lease, int heartbeat, int expected)
        {
            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.LargeFiles).Returns(new LargeFilesConfig { LeaseTimeoutSeconds = lease });
            config.SetupGet(c => c.CollectorSessions).Returns(new CollectorSessionsConfig { HeartbeatSeconds = heartbeat });

            Assert.Equal(expected, HSMServer.ServiceExtensions.ApplicationServiceExtensions.LargeFileOptionsFor(config.Object).LeaseTimeoutSeconds);
        }

        // A removal of an empty part that fails at Release does not reach the connection, where it would drop it.
        [Fact]
        public void AReleaseTheStoreCannotComplete_DoesNotFault()
        {
            var uploads = new Mock<ILargeFileUploads>();
            uploads.Setup(u => u.Release(It.IsAny<UploadLease>())).Throws(new LargeFileStoreException("the part is held by another process"));

            var store = new LargeFileUploadStore(uploads.Object, 1);

            store.Release(new FileUploadLease(Guid.NewGuid(), Guid.NewGuid()));

            uploads.Verify(u => u.Release(It.IsAny<UploadLease>()), Times.Once);
        }

        // The real uploads, with a hold inside the end check (after the store marked the file as being checked) and, on
        // demand, a finish answered Incomplete after the hold.
        private sealed class GatedUploads(ILargeFileUploads inner) : ILargeFileUploads
        {
            private volatile FakeFileUploadStore.Gate _gate;
            private volatile bool _incomplete;


            public FakeFileUploadStore.Gate HoldNextCheck(bool answerIncomplete = false)
            {
                _incomplete = answerIncomplete;
                return _gate = new FakeFileUploadStore.Gate();
            }

            public Task<LargeFileBeginResult> BeginAsync(LargeFileBeginRequest request, CancellationToken ct) => inner.BeginAsync(request, ct);

            public Task<LargeFileAppendResult> AppendAsync(UploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct) =>
                inner.AppendAsync(lease, pieceNumber, storedBytes, digest, ct);

            public Task<bool> DiscardAsync(UploadLease lease, string reason, CancellationToken ct) => inner.DiscardAsync(lease, reason, ct);

            public void Release(UploadLease lease) => inner.Release(lease);

            public bool IsChecking(Guid fileId) => inner.IsChecking(fileId);

            public async Task<LargeFileFinishResult> FinishAsync(UploadLease lease, byte[] wholeHash, ILargeFilePartChecker checker, CancellationToken ct)
            {
                var gate = _gate;
                _gate = null;

                if (gate is not null && _incomplete)
                {
                    _incomplete = false;
                    await gate.PassAsync(ct);
                    return new LargeFileFinishResult(LargeFileFinishOutcome.Incomplete, lease.FileId, null, 0, "forced by the test");
                }

                return await inner.FinishAsync(lease, wholeHash, gate is null ? checker : new GatedChecker(checker, gate), ct);
            }
        }


        private sealed class GatedChecker(ILargeFilePartChecker inner, FakeFileUploadStore.Gate gate) : ILargeFilePartChecker
        {
            public async Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
            {
                await gate.PassAsync(ct);
                return await inner.CheckAsync(part, wholeHash, ct);
            }
        }

        private static TestFile Routed(TestFile file) =>
            new() { Name = file.Name, Bytes = file.Bytes, PieceBytes = file.PieceBytes, Secure = false, Status = 3, Comment = "disk is full", Source = $"send:{Guid.NewGuid():N}" };

        private LargeFileRecord Record(string upload)
        {
            Assert.True(FilePiece.TryParseUploadId(upload, out var fileId));

            return Files.TryGetFile(fileId);
        }

        private Task<SensorApiTestHost> StartHost() => SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), files: _store);

        private async Task<FileConnectionTestClient> OpenFileAsync(SensorApiTestHost host)
        {
            var (session, sessionId) = await TableConnectionTestClient.OpenSessionAsync(host.SessionUri, _key);

            var file = await FileConnectionTestClient.OpenAsync(host.SessionUri, _key, sessionId);

            file.Session = session;
            return file;
        }

        private async Task<BaseSensorModel> CreateFileSensorAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(TestProductsManager.ProductId, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.File,
                TypeIsExplicit = true,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor));
            return sensor;
        }
    }
}
