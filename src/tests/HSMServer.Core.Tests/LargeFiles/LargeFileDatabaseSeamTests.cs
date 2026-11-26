using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
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
    public sealed class LargeFileDatabaseSeamFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileDatabaseSeamTests);
    }


    /// <summary>
    /// The large file store over a database whose reads and deletes are made to fail or leave rows in place (#1883): the
    /// files are removed only on evidence.
    /// </summary>
    [Collection("Database collection")]
    public class LargeFileDatabaseSeamTests : MonitoringCoreTestsBase<LargeFileDatabaseSeamFixture>
    {
        private const int Piece = 65_536;

        private FailingDatabaseCore _database;
        private readonly LargeFileUploadService _service;


        public LargeFileDatabaseSeamTests(LargeFileDatabaseSeamFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new LargeFileUploadService(_database, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _database = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task A_clear_that_leaves_a_row_in_place_keeps_its_file()
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

            _database.ClearSensorValuesLeavesRows = true;
            Assert.True((await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id))).IsOk);

            Assert.NotNull(_database.LargeFiles.TryGetFile(finish.FileId));
            Assert.Equal(0, _database.LargeFiles.Statistics.FilesRemoved);
            Assert.NotNull(_database.GetSensorValue(sensor.Id, finish.ValueTimeUtc.Value.Ticks));
        }

        [Fact]
        public async Task Archives_no_record_names_are_kept_until_a_check_of_the_values_completes_without_a_failure()
        {
            var (sensor, finish) = await UploadAsync("f");
            var store = _database.LargeFiles;
            var stray = StrayArchive(store, sensor.Root.Id, DateTime.UtcNow.AddDays(-2));

            // The rows of the File sensors cannot be read: cannot tell, kept, counted.
            _database.SensorValuesReadFails = true;
            await _service.RunPassAsync(DateTime.UtcNow, atStart: true);

            Assert.True(File.Exists(stray));
            Assert.Equal(1, store.Statistics.StartDisagreements);

            // Kept for the rest of the process, whatever later passes find.
            _database.SensorValuesReadFails = false;
            await _service.RunPassAsync(DateTime.UtcNow, atStart: false);
            Assert.True(File.Exists(stray));
            Assert.NotNull(store.TryGetFile(finish.FileId));
        }

        [Fact]
        public async Task A_start_pass_that_stops_leaves_the_archives_kept_until_a_later_pass_completes_the_check()
        {
            var (sensor, _) = await UploadAsync("f");
            var store = _database.LargeFiles;
            var stray = StrayArchive(store, sensor.Root.Id, DateTime.UtcNow.AddDays(-2));

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RunPassAsync(DateTime.UtcNow, atStart: true, cancelled.Token));
            }

            // The store's own pass alone does not remove it: no check of the values has completed.
            store.RunStorePass();
            Assert.True(File.Exists(stray));

            // A later pass repeats the check, completes it, then removes the orphan.
            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(1, report.Store.OrphanArchivesRemoved);
            Assert.False(File.Exists(stray));
        }

        [Fact]
        public async Task A_verified_file_whose_row_cannot_be_read_is_not_judged_and_kept()
        {
            var (sensor, finish) = await UploadAsync("f");
            await SetKeepHistoryAsync(sensor, TimeSpan.FromDays(1));

            // Its row is gone below the floor, but the read answers that it could not read.
            _database.ClearSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue);
            _database.SensorValueReadFails = true;

            var report = await _service.RunPassAsync(DateTime.UtcNow.AddDays(3), atStart: false);

            Assert.Equal(0, report.FilesRemoved);
            Assert.Equal(0, report.RolledForward);
            Assert.Equal(1, report.Unchecked);
            Assert.NotNull(_database.LargeFiles.TryGetFile(finish.FileId));
        }

        [Fact]
        public async Task The_files_of_a_removed_sensor_stay_while_the_database_does_not_confirm_its_entity_is_gone()
        {
            var (sensor, finish) = await UploadAsync("f");
            var store = _database.LargeFiles;

            _database.SensorEntityReadFails = true;
            await _valuesCache.RemoveSensorAsync(sensor.Id);

            Assert.NotNull(store.TryGetFile(finish.FileId));
            Assert.Equal(0, store.Statistics.FilesRemoved);

            // The pass decides once the database can tell.
            _database.SensorEntityReadFails = false;
            Assert.Equal(1, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).FilesRemoved);
            Assert.Null(store.TryGetFile(finish.FileId));
        }

        private async Task<(HSMServer.Core.Model.BaseSensorModel Sensor, LargeFileFinishResult Finish)> UploadAsync(string name)
        {
            var product = TestProductsManager.ProductId;
            var path = $"lf{Guid.NewGuid():N}"[..12] + "/" + name;
            var registration = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.File,
                TypeIsExplicit = true,
            });
            Assert.True(registration.IsOk, registration.Error);

            var content = new byte[Piece];
            new Random(2).NextBytes(content);

            var begin = await _service.BeginAsync(new LargeFileBeginRequest(product, path, "a.dmp", content.Length, DateTime.UtcNow, null, Piece, Piece), default);
            await _service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default);
            var finish = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), new StoredBytesHashChecker(), default);
            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);

            Assert.True(_valuesCache.TryGetSensorByPath(product, path, out var sensor));
            return (sensor, finish);
        }

        private static string StrayArchive(ILargeFileStore store, Guid productId, DateTime written)
        {
            var folder = Path.Combine(store.Folder, "archives", productId.ToString("N"));
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(path, [1]);
            File.SetLastWriteTimeUtc(path, written);

            return path;
        }

        private async Task SetKeepHistoryAsync(HSMServer.Core.Model.BaseSensorModel sensor, TimeSpan period) =>
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                KeepHistory = new HSMServer.Core.Model.TimeIntervalModel(period.Ticks),
                Initiator = HSMServer.Core.TableOfChanges.InitiatorInfo.System,
            })).IsOk);

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
