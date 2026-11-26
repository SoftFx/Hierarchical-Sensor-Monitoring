using System;
using System.IO;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using HSMServer.Core.DataLayer;
using HSMServer.Model.FileSensors;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    // #1886: what the sensor page reads from the real large file store: a sensor's uploads with their progress, kind and
    // origin, and the record of a file; nothing from a server that never took a file.
    public sealed class StoreLargeFilePageSourceTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-large-page-{Guid.NewGuid():N}");
        private readonly LargeFileStore _store;
        private readonly StoreLargeFilePageSource _source;


        public StoreLargeFilePageSourceTests()
        {
            _store = new LargeFileStore(_folder) { FreeBytes = () => long.MaxValue / 2 };

            var database = new Mock<IDatabaseCore>();
            database.SetupGet(d => d.LargeFiles).Returns(_store);

            _source = new StoreLargeFilePageSource(database.Object);
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
}
