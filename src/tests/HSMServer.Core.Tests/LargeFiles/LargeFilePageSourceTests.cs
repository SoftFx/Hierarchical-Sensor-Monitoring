using System;
using System.IO;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using HSMServer.Core.DataLayer;
using HSMServer.Model.FileSensors;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    // #1886: what the sensor page reads from the real large file store: a sensor's uploads with their progress, kind and
    // origin, and the record of a file; nothing from a server that never took a file.
    public sealed class LargeFilePageSourceTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-large-page-{Guid.NewGuid():N}");
        private readonly LargeFileStore _store;
        private readonly LargeFilePageSource _source;


        public LargeFilePageSourceTests()
        {
            _store = new LargeFileStore(_folder) { FreeBytes = () => long.MaxValue / 2 };

            var database = new Mock<IDatabaseCore>();
            database.SetupGet(d => d.LargeFiles).Returns(_store);

            _source = new LargeFilePageSource(database.Object, downloads: null, passwords: null, log: null);
        }

        public void Dispose()
        {
            _store.Dispose();

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException) { }
        }


        [Fact]
        public void A_store_that_cannot_be_read_lists_no_uploads_and_describes_a_file_as_unavailable()
        {
            var store = new Mock<ILargeFileStore>();
            store.SetupGet(s => s.HasContent).Returns(true);
            store.Setup(s => s.GetFiles(It.IsAny<LargeFileState>())).Throws(new LargeFileStoreException("disk gone"));
            store.Setup(s => s.TryGetFile(It.IsAny<Guid>())).Throws(new LargeFileStoreException("disk gone"));

            var database = new Mock<IDatabaseCore>();
            database.SetupGet(d => d.LargeFiles).Returns(store.Object);

            var source = new LargeFilePageSource(database.Object, downloads: null, passwords: null, log: null);
            var fileId = Guid.NewGuid();

            Assert.Empty(source.GetUploads(Guid.NewGuid()));
            Assert.Equal(LargeFileStoredFacts.Unavailable, source.Describe(fileId).State);
            Assert.Equal(LargeFileRowState.Unavailable, FileSensorPageService.Decide(source.Describe(fileId), hasPassword: true).State);
        }

        [Fact]
        public void A_store_without_content_lists_nothing()
        {
            Assert.Empty(_source.GetUploads(Guid.NewGuid()));
            Assert.Null(_source.Describe(Guid.NewGuid()));
        }

        [Fact]
        public void An_upload_shows_its_pieces_kind_and_origin_and_is_not_a_stored_file()
        {
            var sensor = Guid.NewGuid();
            var key = new LargeFileUploadKey(Guid.NewGuid(), sensor, "big.csv", 3 * 65_536, DateTime.UtcNow, null, Secure: false);

            var begin = _store.Begin(key, "csv", 65_536, 65_536, "TTS", "10.0.0.7");
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);
            Assert.Equal(LargeFileAppendOutcome.Stored, _store.Append(begin.Lease, 0, new byte[10], new byte[32]).Outcome);

            var upload = Assert.Single(_source.GetUploads(sensor));

            Assert.Equal(("big.csv", 1L, 3L), (upload.Name, upload.PiecesReceived, upload.PiecesTotal));
            Assert.False(upload.IsSecure);
            Assert.False(upload.IsChecking);
            Assert.Equal(("TTS", "10.0.0.7"), (upload.Application, upload.Host));
            Assert.Equal("uploading, 33%", FileSensorPageService.Decide(upload).Text);

            Assert.Empty(_source.GetUploads(Guid.NewGuid()));
            Assert.Null(_source.Describe(begin.FileId));
        }
    }

    // #1886: the page's source over the download step (#1885): its description, its words, its web link and its log.
    public sealed class LargeFilePageSourceArchiveTests : IDisposable
    {
        private readonly HSMServer.Core.Tests.LargeFiles.Downloads.LargeFileWorld _world = new();
        private readonly LargeFilePageSource _source;


        public LargeFilePageSourceArchiveTests()
        {
            var database = new Mock<IDatabaseCore>();
            _source = new LargeFilePageSource(database.Object, _world.CreateService(), _world.Passwords.Object, _world.Log.Object);
        }

        public void Dispose() => _world.Dispose();


        [Fact]
        public void A_packed_file_is_ready_with_the_web_link_of_its_archive()
        {
            var file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, HSMServer.Core.Tests.LargeFiles.Downloads.LargeFileWorld.ArchiveBytes());

            var facts = _source.Describe(file.FileId);

            Assert.Equal(("packed", _world.SensorA.Id, true), (facts.State, facts.SensorId, facts.IsSecure));
            Assert.Equal(LargeFileDownloadService.WebDownloadPath(file.FileId, file.ArchiveId.Value), facts.DownloadUrl);
            Assert.Equal(LargeFileRowState.Ready, FileSensorPageService.Decide(facts, hasPassword: true).State);
        }

        [Fact]
        public void A_secure_verified_file_of_a_product_without_a_password_waits_in_the_download_steps_words()
        {
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(false);
            var file = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified);

            var facts = _source.Describe(file.FileId);

            Assert.Equal("verified", facts.State);
            Assert.Null(facts.DownloadUrl);
            Assert.Equal(LargeFileDownloadService.WaitingForPasswordReason, facts.NotReadyReason);
            Assert.Equal(LargeFileRowState.WaitingForPassword, FileSensorPageService.Decide(facts, _source.HasDumpPassword(_world.ProductA.Id)).State);
        }

        [Fact]
        public void A_not_secure_verified_file_never_waits_for_the_password()
        {
            _world.Passwords.Setup(p => p.HasPassword(_world.ProductA.Id)).Returns(false);
            var file = _world.Store.AddInState(_world.ProductA.Id, _world.SensorA.Id, LargeArchiveFileState.Verified, secure: false);

            var facts = _source.Describe(file.FileId);

            Assert.False(facts.IsSecure);
            Assert.Equal(LargeFileRowState.Packing, FileSensorPageService.Decide(facts, hasPassword: false).State);
        }

        [Fact]
        public void A_file_of_a_removed_sensor_is_missing()
        {
            var file = _world.Store.AddPacked(_world.ProductA.Id, _world.SensorA.Id, HSMServer.Core.Tests.LargeFiles.Downloads.LargeFileWorld.ArchiveBytes());
            _world.RemoveSensor(_world.SensorA);

            Assert.Null(_source.Describe(file.FileId));
        }

        [Fact]
        public void The_downloads_of_a_file_name_the_user_or_the_token_and_the_channel()
        {
            var fileId = Guid.NewGuid();
            var time = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

            _world.Log.Setup(l => l.ReadFile(_world.ProductA.Id, fileId, 5)).Returns(
            [
                new DownloadRecord { TimeUtc = time, UserName = "viewerA", Channel = DownloadChannel.Web },
                new DownloadRecord { TimeUtc = time.AddMinutes(-1), UserName = "managerA", TokenId = Guid.NewGuid(), TokenName = "ci", Channel = DownloadChannel.Api },
            ]);

            var downloads = _source.GetDownloads(_world.ProductA.Id, fileId, 5);

            Assert.Equal([new LargeFileDownloadFacts(time, "viewerA", "web"), new LargeFileDownloadFacts(time.AddMinutes(-1), "managerA (token ci)", "API")], downloads);
        }
    }
}
