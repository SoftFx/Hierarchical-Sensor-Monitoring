using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.StorageTests
{
    // #1885: a read of the download log iterates a LevelDB snapshot outside the lock a record takes, so a long read (a page
    // of the log) never makes a download wait for its record; the database is closed only after the last read finished.
    public sealed class DownloadLogCollectionConcurrencyTests : IDisposable
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"hsm-download-log-collection-{Guid.NewGuid():N}");
        private readonly DownloadLogCollection _collection;

        private readonly ManualResetEventSlim _paused = new();
        private readonly ManualResetEventSlim _resume = new();


        public DownloadLogCollectionConcurrencyTests()
        {
            _collection = new DownloadLogCollection(_path);

            _collection.Put(Record(1));
            _collection.Put(Record(2));

            // The first row a read scans pauses it until the test resumes it.
            var first = 0;
            _collection.OnRowScanned = () =>
            {
                if (Interlocked.Exchange(ref first, 1) == 0)
                {
                    _paused.Set();
                    _resume.Wait(Guard);
                }
            };
        }

        public void Dispose()
        {
            _resume.Set();
            _collection.Dispose();
            _paused.Dispose();
            _resume.Dispose();

            try
            {
                Directory.Delete(_path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        [Fact]
        public async Task ARecord_IsWrittenWhileAReadIsInTheMiddleOfItsScan()
        {
            var read = Task.Run(() => _collection.Read(0, long.MaxValue, null, 100));
            Assert.True(_paused.Wait(Guard), "the read did not reach its first row");

            var put = Task.Run(() => _collection.Put(Record(3)));

            // The record completes while the read is still paused inside its scan.
            Assert.True(put.Wait(Guard), "the record waited for the read");
            Assert.False(read.IsCompleted);

            _resume.Set();

            // The read sees the snapshot it started on; the next read sees the new record too.
            Assert.Equal([2L, 1L], (await read).Select(r => r.TimeTicks));
            Assert.Equal([3L, 2L, 1L], _collection.Read(0, long.MaxValue, null, 100).Select(r => r.TimeTicks));
        }

        [Fact]
        public async Task ADispose_WaitsForAReadInTheMiddleOfItsScan()
        {
            using var disposing = new ManualResetEventSlim();
            _collection.OnWaitingForReaders = disposing.Set;

            var read = Task.Run(() => _collection.Read(0, long.MaxValue, null, 100));
            Assert.True(_paused.Wait(Guard), "the read did not reach its first row");

            var dispose = Task.Run(_collection.Dispose);
            Assert.True(disposing.Wait(Guard), "the dispose did not wait for the read");
            Assert.False(dispose.IsCompleted);

            _resume.Set();

            Assert.Equal([2L, 1L], (await read).Select(r => r.TimeTicks));
            Assert.True(dispose.Wait(Guard), "the dispose did not finish after the read");
            Assert.Throws<ObjectDisposedException>(() => _collection.Put(Record(4)));
            Assert.Throws<ObjectDisposedException>(() => _collection.Read(0, long.MaxValue, null, 100));
        }


        private static DownloadRecordEntity Record(long ticks) => new()
        {
            Id = Guid.NewGuid(),
            TimeTicks = ticks,
            ProductId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            FileName = "dump.dmp",
        };
    }
}
