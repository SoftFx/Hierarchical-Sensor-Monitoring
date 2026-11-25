using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMDatabase.AccessManager.LargeFiles;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    public sealed class LargeFileSilentClearFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileSilentClearTests);
    }


    /// <summary>
    /// A clear whose delete of rows failed without a word (#1883): the files of rows that are still there are kept.
    /// </summary>
    [Collection("Database collection")]
    public class LargeFileSilentClearTests : MonitoringCoreTestsBase<LargeFileSilentClearFixture>
    {
        private const int Piece = 65_536;

        private FailingDatabaseCore _database;
        private readonly LargeFileUploadService _service;


        public LargeFileSilentClearTests(LargeFileSilentClearFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new LargeFileUploadService(_database, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _database = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task A_clear_whose_delete_failed_keeps_the_files_of_the_rows_still_there()
        {
            var product = TestProductsManager.ProductId;
            var path = $"lf{Guid.NewGuid():N}"[..12] + "/f";
            var registration = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.File,
                TypeIsExplicit = true,
            });
            Assert.True(registration.IsOk, registration.Error);

            var content = new byte[Piece];
            new Random(1).NextBytes(content);

            var begin = await _service.BeginAsync(new LargeFileBeginRequest(product, path, "a.dmp", content.Length, DateTime.UtcNow, null, Piece, Piece), default);
            await _service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default);
            var finish = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), new StoredBytesHashChecker(), default);
            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);

            Assert.True(_valuesCache.TryGetSensorByPath(product, path, out var sensor));

            _database.ClearSensorValuesSilentlyFails = true;
            Assert.True((await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id))).IsOk);

            Assert.NotNull(_database.LargeFiles.TryGetFile(finish.FileId));
            Assert.Equal(0, _database.LargeFiles.Statistics.FilesRemoved);
            Assert.NotNull(_database.GetSensorValue(sensor.Id, finish.ValueTimeUtc.Value.Ticks));
        }

        [Fact]
        public async Task A_file_of_a_sensor_missing_from_the_tree_is_kept_while_the_database_cannot_tell_and_removed_once_it_can()
        {
            var product = TestProductsManager.ProductId;
            var sensorId = Guid.NewGuid(); // in neither the tree nor the database

            var content = new byte[Piece];
            var store = _database.LargeFiles;
            var begin = store.Begin(new LargeFileUploadKey(product, sensorId, "x.dmp", content.Length, DateTime.UtcNow, null), "dmp", Piece, Piece);
            store.Append(begin.Lease, 0, content, SHA256.HashData(content));
            store.StartFinish(begin.Lease);
            store.MarkVerified(begin.FileId, SHA256.HashData(content), DateTime.UtcNow);
            store.EndFinish(begin.Lease);

            _database.SensorEntityReadFails = true;
            var kept = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(0, kept.FilesRemoved);
            Assert.NotNull(store.TryGetFile(begin.FileId));
            Assert.Equal(1, store.Statistics.FilesOfUnloadedSensors);

            _database.SensorEntityReadFails = false;
            var removed = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(1, removed.FilesRemoved);
            Assert.Null(store.TryGetFile(begin.FileId));
        }
    }
}
