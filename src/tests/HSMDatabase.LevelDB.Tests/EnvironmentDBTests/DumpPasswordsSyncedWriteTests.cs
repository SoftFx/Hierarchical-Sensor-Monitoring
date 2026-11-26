using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB.DatabaseImplementations;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.EnvironmentDBTests
{
    // #1885: a dump password row is written and removed with a synced write, so a version that was answered as saved is
    // on the disk before the answer: after a power loss the next version is never one an archive already carries.
    //
    // A real LevelDB in a temp directory, behind an adapter that records which kind of write each key went through:
    // LevelDB itself gives a test no way to observe the sync.
    public sealed class DumpPasswordsSyncedWriteTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hsm-dump-password-rows-{Guid.NewGuid():N}");
        private readonly RecordingAdapter _adapter;
        private readonly EnvironmentDatabaseWorker _worker;


        public DumpPasswordsSyncedWriteTests()
        {
            Directory.CreateDirectory(_dbPath);

            _adapter = new RecordingAdapter(_dbPath);
            _worker = new EnvironmentDatabaseWorker(_adapter);
        }


        public void Dispose()
        {
            _worker.Dispose();

            try
            {
                Directory.Delete(_dbPath, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        [Fact]
        public void PutDumpPasswords_IsASyncedWrite_AndReadsBack()
        {
            var productId = Guid.NewGuid();

            _worker.PutDumpPasswords(Row(productId, 3));

            Assert.Equal([Key(productId)], _adapter.SyncedPuts);
            Assert.Empty(_adapter.PlainPuts);
            Assert.Equal(3, _worker.ReadAllDumpPasswords().Single(e => e.ProductId == productId).Versions.Single().Version);
        }

        [Fact]
        public void RemoveDumpPasswords_IsASyncedDelete_AndTheRowIsGone()
        {
            var productId = Guid.NewGuid();
            _worker.PutDumpPasswords(Row(productId, 1));

            _worker.RemoveDumpPasswords(productId);

            Assert.Equal([Key(productId)], _adapter.SyncedDeletes);
            Assert.Empty(_adapter.PlainDeletes);
            Assert.DoesNotContain(_worker.ReadAllDumpPasswords(), e => e.ProductId == productId);
        }


        private static string Key(Guid productId) => $"DumpPasswords_{productId}";

        private static ProductDumpPasswordsEntity Row(Guid productId, int version) => new()
        {
            ProductId = productId,
            Versions = [new DumpPasswordVersionEntity { Version = version, Password = "row-value", SetTicks = 1, SetBy = "test" }],
        };


        private sealed class RecordingAdapter(string name) : LevelDBDatabaseAdapter(name)
        {
            public List<string> PlainPuts { get; } = [];

            public List<string> SyncedPuts { get; } = [];

            public List<string> PlainDeletes { get; } = [];

            public List<string> SyncedDeletes { get; } = [];


            public override void Put(byte[] key, byte[] value)
            {
                PlainPuts.Add(Encoding.UTF8.GetString(key));
                base.Put(key, value);
            }

            public override void PutSynced(byte[] key, byte[] value)
            {
                SyncedPuts.Add(Encoding.UTF8.GetString(key));
                base.PutSynced(key, value);
            }

            public override void Delete(byte[] key)
            {
                PlainDeletes.Add(Encoding.UTF8.GetString(key));
                base.Delete(key);
            }

            public override void DeleteSynced(byte[] key)
            {
                SyncedDeletes.Add(Encoding.UTF8.GetString(key));
                base.DeleteSynced(key);
            }
        }
    }
}
