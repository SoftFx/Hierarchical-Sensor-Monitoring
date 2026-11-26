using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using System.Collections.Concurrent;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.Cache;
using HSMServer.Core.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Managers;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    public sealed class LargeFileUploadServiceFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(LargeFileUploadServiceTests);
    }


    /// <summary>
    /// The uploads of large files and the value they become (#1883): the sensor a begin may name, the value written on a
    /// verified finish, its unique time, the removal of files whose values are gone, the roll-forward of a missing row,
    /// the check of the rows at start, the move and merge refusal and the UI's new value. Every test has its own database.
    /// </summary>
    [Collection("Database collection")]
    public class LargeFileUploadServiceTests : MonitoringCoreTestsBase<LargeFileUploadServiceFixture>
    {
        private const int Piece = 65_536;
        private static readonly DateTime Modified = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly string _root = $"lf{Guid.NewGuid():N}"[..12];
        private readonly LargeFileUploadService _service;
        private readonly StoredBytesHashChecker _checker = new();


        public LargeFileUploadServiceTests(LargeFileUploadServiceFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new LargeFileUploadService(_databaseCoreManager.DatabaseCore, _valuesCache, new LargeFileOptions { MinFreeDiskMb = 0 });
        }


        // === The sensor a begin names ===

        [Fact]
        public async Task A_begin_is_refused_for_a_missing_sensor_another_type_a_blocked_sensor_an_unknown_product_and_a_path_not_normalized()
        {
            var file = await CreateFileSensorAsync("f");
            var integer = await CreateIntSensorAsync("i");
            var blocked = await CreateFileSensorAsync("b");
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = blocked.Id, State = SensorState.Blocked, Initiator = InitiatorInfo.System })).IsOk);

            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await BeginAsync("missing", 10)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await BeginAsync(integer.Path, 10, raw: true)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await BeginAsync(blocked.Path, 10, raw: true)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await _service.BeginAsync(Request(file.Path, 10) with { ProductId = Guid.NewGuid() }, default)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await BeginAsync("/" + file.Path, 10, raw: true)).Outcome);

            var started = await BeginAsync(file.Path, 10, raw: true);
            Assert.Equal(LargeFileBeginOutcome.Started, started.Outcome);
            Assert.Equal(file.Id, Store.TryGetFile(started.FileId).SensorId);
            Assert.Equal("dump.dmp", Store.TryGetFile(started.FileId).Name);
            Assert.Equal("dmp", Store.TryGetFile(started.FileId).Extension);

            await Assert.ThrowsAsync<ArgumentException>(() => _service.BeginAsync(Request(file.Path, 10) with { Name = "" }, default));
            await Assert.ThrowsAsync<ArgumentException>(() => _service.BeginAsync(Request(file.Path, 10) with { SourceIdentity = new string('x', 257) }, default));
            await Assert.ThrowsAsync<ArgumentException>(() => _service.BeginAsync(Request(file.Path, 10) with { ClientName = new string('x', 257) }, default));
            await Assert.ThrowsAsync<ArgumentException>(() => _service.BeginAsync(Request(file.Path, 10) with { RemoteAddress = new string('x', 257) }, default));
        }

        [Fact]
        public async Task Building_the_service_and_a_refused_begin_create_nothing_on_the_disk()
        {
            await CreateFileSensorAsync("f");
            _ = await BeginAsync("missing", 10);
            await _service.RunPassAsync(DateTime.UtcNow, atStart: true);

            Assert.False(Directory.Exists(Store.Folder));
            Assert.False(Directory.Exists(Path.Combine(_databaseCoreManager.DatabaseCore.DatabaseSettings.DatabaseFolder, "LargeFiles")));
        }

        // === Append, finish, the value ===

        [Fact]
        public async Task A_verified_upload_becomes_the_sensors_value_without_its_content_and_a_begin_of_it_answers_already_stored()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(2 * Piece + 5);
            var verifiedEvents = new List<LargeFileRecord>();
            Store.FileVerified += verifiedEvents.Add;

            var finish = await UploadAsync(sensor, content);

            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);
            Assert.NotNull(finish.ValueTimeUtc);

            var record = Store.TryGetFile(finish.FileId);
            Assert.Equal(LargeFileState.Verified, record.State);
            Assert.Equal(finish.ValueTimeUtc, record.ValueTimeUtc);
            Assert.Equal(SHA256.HashData(content), record.Hash);
            Assert.Single(verifiedEvents);

            var last = Assert.IsType<LargeFileValue>(sensor.LastValue);
            Assert.Equal(finish.FileId, last.FileId);
            Assert.Empty(last.Value);
            Assert.Equal(content.Length, last.OriginalSize);
            Assert.Equal("dump.dmp", last.Name);
            Assert.Equal("dmp", last.Extension);
            Assert.Equal(Modified, last.SourceModified);
            Assert.Equal(SHA256.HashData(content), last.Hash);
            Assert.Equal(finish.ValueTimeUtc, last.Time);

            // The history read: the description, never the content.
            var rows = await ReadRowsAsync(sensor);
            var row = Assert.IsType<LargeFileValue>(Assert.Single(rows));
            Assert.Empty(row.Value);
            Assert.Equal(finish.FileId, row.FileId);

            Assert.Equal(LargeFileBeginOutcome.AlreadyStored, (await BeginAsync(sensor.Path, content.Length, raw: true)).Outcome);
        }

        [Fact]
        public async Task A_wrong_hash_discards_the_upload_and_writes_no_value()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            var finish = await _service.FinishAsync(begin.Lease, new byte[32], _checker, default);

            Assert.Equal(LargeFileFinishOutcome.Mismatch, finish.Outcome);
            Assert.Equal("The SHA-256 of the stored bytes is not the hash sent.", finish.Error);
            Assert.Null(Store.TryGetFile(begin.FileId));
            Assert.Null(sensor.LastValue);
            Assert.Empty(await ReadRowsAsync(sensor));
            Assert.Equal(1, Store.Statistics.PartsRemoved);

            var again = await BeginAsync(sensor.Path, content.Length, raw: true);
            Assert.Equal(LargeFileBeginOutcome.Started, again.Outcome);
            Assert.NotEqual(begin.FileId, again.FileId);
        }

        [Fact]
        public async Task A_finish_before_every_piece_is_in_is_incomplete_and_a_second_finish_during_the_check_answers_checking()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(2 * Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content, count: 1);

            var incomplete = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default);
            Assert.Equal(LargeFileFinishOutcome.Incomplete, incomplete.Outcome);
            Assert.Equal(1, incomplete.NextPiece);

            await AppendAllAsync(begin.Lease, content, from: 1);

            var gate = new BlockingChecker(_checker);
            var first = _service.FinishAsync(begin.Lease, SHA256.HashData(content), gate, default);
            await gate.Entered.Task;

            var second = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default);
            Assert.Equal(LargeFileFinishOutcome.Checking, second.Outcome);
            Assert.True(_service.IsChecking(begin.FileId));
            Assert.Equal(LargeFileBeginOutcome.Checking, (await BeginAsync(sensor.Path, content.Length, raw: true)).Outcome);

            gate.Release.SetResult();
            Assert.Equal(LargeFileFinishOutcome.Verified, (await first).Outcome);
        }

        [Fact]
        public async Task A_sensor_removed_during_the_upload_takes_its_part_at_once_and_one_removed_during_the_check_ends_it_as_sensor_gone()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            await _valuesCache.RemoveSensorAsync(sensor.Id);

            // The part went with the sensor: the upload is gone, and a new begin is refused for the sensor.
            Assert.Null(Store.TryGetFile(begin.FileId));
            Assert.Equal(LargeFileAppendOutcome.Discarded, (await _service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default)).Outcome);
            Assert.Equal(LargeFileFinishOutcome.LeaseLost, (await _service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.SensorRefused, (await BeginAsync(sensor.Path, content.Length, raw: true)).Outcome);

            // Removed while its check runs: the removal leaves the file being checked, the finish discards it.
            var other = await CreateFileSensorAsync("g");
            var second = await BeginAsync(other.Path, content.Length, raw: true);
            await AppendAllAsync(second.Lease, content);

            var gate = new BlockingChecker(_checker);
            var finishing = _service.FinishAsync(second.Lease, SHA256.HashData(content), gate, default);
            await gate.Entered.Task;

            await _valuesCache.RemoveSensorAsync(other.Id);
            gate.Release.SetResult();

            Assert.Equal(LargeFileFinishOutcome.SensorGone, (await finishing).Outcome);
            Assert.Null(Store.TryGetFile(second.FileId));
        }

        [Fact]
        public async Task The_value_and_the_record_keep_the_checkers_content_hash_not_the_hash_the_collector_sent()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var contentHash = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            var finish = await _service.FinishAsync(begin.Lease, new byte[32], new FixedChecker(new LargeFilePartCheck(true, contentHash, null, null)), default);

            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);
            Assert.Equal(contentHash, Store.TryGetFile(begin.FileId).Hash);
            Assert.Equal(contentHash, Assert.IsType<LargeFileValue>(sensor.LastValue).Hash);

            // The lease ended with the finish.
            Assert.Equal(LargeFileAppendOutcome.LeaseLost, (await _service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default)).Outcome);
        }

        [Fact]
        public async Task A_check_that_fails_leaves_the_upload_as_it_was_and_not_busy()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            await Assert.ThrowsAsync<IOException>(() => _service.FinishAsync(begin.Lease, new byte[32], new ThrowingChecker(), default));

            var record = Store.TryGetFile(begin.FileId);
            Assert.Equal(LargeFileState.Uploading, record.State);
            Assert.Equal(1, record.PieceCount);

            var again = await BeginAsync(sensor.Path, content.Length, raw: true);
            Assert.Equal(LargeFileBeginOutcome.Continue, again.Outcome);
            Assert.Equal(1, again.NextPiece);
        }

        [Fact]
        public async Task Two_large_values_accepted_in_one_tick_get_two_times_and_a_tick_held_by_a_row_is_skipped()
        {
            var sensor = await CreateFileSensorAsync("f");
            var now = DateTime.UtcNow.AddMinutes(1);
            _valuesCache.LargeFileClock = () => now;

            // An ordinary file value already holds the tick "now".
            _databaseCoreManager.DatabaseCore.AddSensorValue(sensor.Id, new FileValue { Time = now, ReceivingTime = now, Value = [1], OriginalSize = 1, Name = "small" });

            var first = await UploadAsync(sensor, Content(Piece), name: "a.dmp");
            var second = await UploadAsync(sensor, Content(Piece), name: "b.dmp");

            Assert.Equal(now.AddTicks(1), first.ValueTimeUtc);
            Assert.Equal(now.AddTicks(2), second.ValueTimeUtc);

            var rows = await ReadRowsAsync(sensor);
            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows.OfType<LargeFileValue>().Count());
            Assert.Single(rows, r => r is not LargeFileValue);
        }

        // === Removal: with the sensor, the product, a clear, retention; the pass ===

        [Fact]
        public async Task Removing_the_sensor_removes_its_files_at_once()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));
            var uploading = await BeginAsync(sensor.Path, 3 * Piece, raw: true, name: "half.dmp");
            await _service.AppendAsync(uploading.Lease, 0, Content(Piece), SHA256.HashData(Content(Piece)), default);

            await _valuesCache.RemoveSensorAsync(sensor.Id);

            // At once, with the sensor; the pass finds nothing left.
            Assert.Null(Store.TryGetFile(verified.FileId));
            Assert.Null(Store.TryGetFile(uploading.FileId));
            Assert.Equal(1, Store.Statistics.FilesRemoved);
            Assert.Equal(1, Store.Statistics.PartsRemoved);

            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(0, report.FilesRemoved);
            Assert.Empty(Directory.EnumerateFiles(Store.Folder, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Removing_the_product_removes_its_files_at_once()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));

            await _valuesCache.RemoveProductAsync(_product);

            Assert.Null(Store.TryGetFile(verified.FileId));
            Assert.Equal(1, Store.Statistics.FilesRemoved);
        }

        [Fact]
        public async Task Clearing_the_history_removes_the_files_of_the_cleared_values_at_once()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));

            Assert.True((await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id))).IsOk);

            Assert.Null(Store.TryGetFile(verified.FileId));
            Assert.Equal(1, Store.Statistics.FilesRemoved);
            Assert.Equal(0, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).FilesRemoved);
        }

        [Fact]
        public async Task A_value_removed_by_retention_takes_its_file_with_it()
        {
            var sensor = await CreateFileSensorAsync("f");
            _valuesCache.LargeFileClock = () => DateTime.UtcNow.AddDays(-10);
            var old = await UploadAsync(sensor, Content(Piece), name: "old.dmp");
            _valuesCache.LargeFileClock = () => DateTime.UtcNow;
            var recent = await UploadAsync(sensor, Content(Piece), name: "new.dmp");

            await SetKeepHistoryAsync(sensor, TimeSpan.FromDays(1));
            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.Null(Store.TryGetFile(old.FileId));
            Assert.NotNull(Store.TryGetFile(recent.FileId));
            Assert.Single(await ReadRowsAsync(sensor));
        }

        [Fact]
        public async Task A_pass_removes_a_file_whose_row_is_gone_below_the_history_floor_and_writes_again_one_inside_the_kept_range()
        {
            var sensor = await CreateFileSensorAsync("f");
            _valuesCache.LargeFileClock = () => DateTime.UtcNow.AddDays(-10);
            var old = await UploadAsync(sensor, Content(Piece), name: "old.dmp");
            _valuesCache.LargeFileClock = () => DateTime.UtcNow;
            var recent = await UploadAsync(sensor, Content(Piece), name: "new.dmp");
            await SetKeepHistoryAsync(sensor, TimeSpan.FromDays(1));

            // The rows are gone and nothing told the store (a removal the pass has to find: a crash after the delete of a
            // clear, a restart that forgot the clear's floor, or the crash between a verified record and its row).
            _databaseCoreManager.DatabaseCore.ClearSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue);

            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(1, report.FilesRemoved);
            Assert.Equal(1, report.RolledForward);
            Assert.Null(Store.TryGetFile(old.FileId));
            Assert.Equal(1, Store.Statistics.RollForwards);

            var row = Assert.IsType<LargeFileValue>(Assert.Single(await ReadRowsAsync(sensor)));
            Assert.Equal(recent.FileId, row.FileId);
            Assert.Equal(recent.ValueTimeUtc, row.Time);

            // The next pass finds everything in place.
            var again = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);
            Assert.Equal(0, again.RolledForward);
            Assert.Equal(0, again.FilesRemoved);
        }

        [Fact]
        public async Task A_row_at_the_values_time_that_is_not_the_file_is_reported_and_the_file_kept()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));
            var time = verified.ValueTimeUtc.Value;

            _databaseCoreManager.DatabaseCore.AddSensorValue(sensor.Id, new FileValue { Time = time, ReceivingTime = time, Value = [1], OriginalSize = 1 });

            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(1, report.RowDisagreements);
            Assert.Equal(1, Store.Statistics.RowDisagreements);
            Assert.NotNull(Store.TryGetFile(verified.FileId));
        }

        [Fact]
        public async Task A_verified_record_whose_row_was_never_written_is_rolled_forward_after_a_restart()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            // The crash between the verified record and the row: the record says Verified, no row exists.
            Assert.Equal(LargeFileFinishStart.Ready, Store.StartFinish(begin.Lease).Outcome);
            var time = DateTime.UtcNow.AddSeconds(-5);
            Store.MarkVerified(begin.FileId, SHA256.HashData(content), time);

            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            _databaseCoreManager.DatabaseCore.Dispose();

            var (database, cache) = Restart(settings);
            await using var _ = cache;
            var service = new LargeFileUploadService(database, cache, new LargeFileOptions { MinFreeDiskMb = 0 });

            var report = await service.RunPassAsync(DateTime.UtcNow, atStart: true);

            Assert.Equal(1, report.RolledForward);
            Assert.Equal(0, report.RowsWithoutRecord);

            var restarted = cache.GetSensor(sensor.Id);
            var value = Assert.IsType<LargeFileValue>(restarted.LastValue);
            Assert.Equal(begin.FileId, value.FileId);
            Assert.Equal(time, value.Time);
            Assert.NotNull(database.GetSensorValue(sensor.Id, time.Ticks));
        }

        [Fact]
        public async Task At_start_a_large_value_whose_file_has_no_record_is_reported_and_kept()
        {
            var sensor = await CreateFileSensorAsync("f");
            var ordinary = await CreateFileSensorAsync("plain");
            var time = DateTime.UtcNow.AddMinutes(-1);

            _databaseCoreManager.DatabaseCore.AddSensorValue(ordinary.Id, new FileValue { Time = time, ReceivingTime = time, Value = [1, 2], OriginalSize = 2 });
            _databaseCoreManager.DatabaseCore.AddSensorValue(sensor.Id, new LargeFileValue
            {
                Time = time,
                ReceivingTime = time,
                Value = [],
                OriginalSize = 10,
                FileId = Guid.NewGuid(),
                Hash = new byte[32],
                Name = "lost.dmp",
            });

            // Not scanned while the store never took a file (the folder may come from a bind mount).
            Assert.Equal(0, (await _service.RunPassAsync(DateTime.UtcNow, atStart: true)).RowsWithoutRecord);

            Assert.Equal(LargeFileBeginOutcome.Started, (await BeginAsync(ordinary.Path, Piece, raw: true)).Outcome);
            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: true);

            Assert.Equal(1, report.RowsWithoutRecord);
            Assert.Equal(1, Store.Statistics.StartDisagreements);
            Assert.Single(await ReadRowsAsync(sensor));

            // Only at start.
            Assert.Equal(0, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).RowsWithoutRecord);
        }

        // === After the review of the branch ===

        [Fact]
        public async Task A_file_whose_sensor_left_the_tree_but_not_the_database_is_kept_and_reported()
        {
            // A sensor the tree did not build at start, or whose removal failed after it left the tree: in the database still.
            var entity = EntitiesFactory.BuildSensorEntity(name: "unloaded", parent: _product.ToString(), type: (byte)SensorType.File);
            _databaseCoreManager.DatabaseCore.AddSensor(entity);
            var sensorId = Guid.Parse(entity.Id);

            var content = Content(Piece);
            var begin = Store.Begin(new LargeFileUploadKey(_product, sensorId, "x.dmp", content.Length, Modified, null), "dmp", Piece, Piece);
            Store.Append(begin.Lease, 0, content, SHA256.HashData(content));
            Store.StartFinish(begin.Lease);
            Store.MarkVerified(begin.FileId, SHA256.HashData(content), DateTime.UtcNow);
            Store.EndFinish(begin.Lease);

            var kept = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(0, kept.FilesRemoved);
            Assert.NotNull(Store.TryGetFile(begin.FileId));
            Assert.Equal(1, Store.Statistics.FilesOfUnloadedSensors);

            // Gone from the database too: removed.
            _databaseCoreManager.DatabaseCore.RemoveSensorWithMetadata(sensorId);
            var removed = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(1, removed.FilesRemoved);
            Assert.Null(Store.TryGetFile(begin.FileId));
        }

        [Fact]
        public async Task A_verified_file_is_alerted_once_like_any_new_value_and_its_roll_forward_is_not_alerted_again()
        {
            var sensor = await CreateFileSensorAsync("f");
            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("tests"),
                Policies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.NewGuid(),
                        Template = "new file",
                        Destination = new PolicyDestinationUpdate(),
                        Conditions = [new PolicyConditionUpdate(PolicyOperation.ReceivedNewValue, PolicyProperty.NewSensorData, new TargetValue(TargetType.Const, ""))],
                    },
                ],
            });
            Assert.True(update.IsOk, update.Error);

            // An alert message leaves the cache only for a product in a folder.
            var folder = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = _product, FolderId = Guid.NewGuid() }, default);
            Assert.True(folder.IsOk, folder.Error);

            var alerts = new ConcurrentQueue<AlertMessage>();
            void Record(AlertMessage message)
            {
                if (message.SensorId == sensor.Id)
                    alerts.Enqueue(message);
            }

            _valuesCache.NewAlertMessageEvent += Record;

            try
            {
                // Delivered on the product's queue inside the finish, before it answers: no waiting needed.
                var verified = await UploadAsync(sensor, Content(Piece));
                Assert.Single(alerts);

                // The row lost and written again from the record, on the same queue inside the pass: shown, not alerted again.
                _databaseCoreManager.DatabaseCore.ClearSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue);
                Assert.Equal(1, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).RolledForward);

                Assert.Single(alerts);
                Assert.NotNull(Store.TryGetFile(verified.FileId));
            }
            finally
            {
                _valuesCache.NewAlertMessageEvent -= Record;
            }
        }


        // #1886: the default alert of a sensor registered for large files, on the real upload path: one message per
        // verified file with its name, size and page; nothing for a half-uploaded file or a file whose check failed.
        [Fact]
        public async Task A_sensor_registered_for_large_files_sends_one_message_per_verified_file_and_none_for_the_others()
        {
            var sensor = await CreateFileSensorAsync("registered", DefaultAlertsOptions.LargeFiles);
            var policy = Assert.Single(sensor.Policies);
            Assert.Equal(SensorPolicyCollection.LargeFileAlertTemplate, policy.Template);

            // An alert message leaves the cache only for a product in a folder.
            var folder = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = _product, FolderId = Guid.NewGuid() }, default);
            Assert.True(folder.IsOk, folder.Error);

            var alerts = new ConcurrentQueue<AlertMessage>();
            void Record(AlertMessage message)
            {
                if (message.SensorId == sensor.Id)
                    alerts.Enqueue(message);
            }

            _valuesCache.NewAlertMessageEvent += Record;

            try
            {
                // Half uploaded: one piece of two, then the connection is gone.
                var half = await BeginAsync(sensor.Path, 2 * Piece, raw: true, name: "half.dmp");
                Assert.Equal(LargeFileBeginOutcome.Started, half.Outcome);
                await AppendAllAsync(half.Lease, Content(2 * Piece), count: 1);
                _service.Release(half.Lease);

                // Every piece in, but the check fails: discarded, no value.
                var bad = await BeginAsync(sensor.Path, Piece, raw: true, name: "bad.dmp");
                await AppendAllAsync(bad.Lease, Content(Piece));
                var mismatch = await _service.FinishAsync(bad.Lease, SHA256.HashData(Content(Piece)),
                    new FixedChecker(new LargeFilePartCheck(false, null, 0, "test")), default);
                Assert.Equal(LargeFileFinishOutcome.Mismatch, mismatch.Outcome);

                Assert.Empty(alerts);

                await UploadAsync(sensor, Content(Piece), name: "first.dmp");
                await UploadAsync(sensor, Content(Piece + 1), name: "second.dmp");

                var texts = alerts.SelectMany(message => message).Select(alert => alert.LastComment).ToList();

                Assert.Equal(2, alerts.Count);
                Assert.Equal(2, texts.Count);
                Assert.Contains(texts, text => text.Contains("new file first.dmp (") && text.Contains($"/Home/{sensor.Id}"));
                Assert.Contains(texts, text => text.Contains("new file second.dmp (") && text.Contains($"/Home/{sensor.Id}"));
            }
            finally
            {
                _valuesCache.NewAlertMessageEvent -= Record;
            }
        }

        [Fact]
        public async Task A_file_sensor_without_the_flag_gets_no_alert_and_sends_nothing_for_its_large_files()
        {
            var sensor = await CreateFileSensorAsync("plain");
            Assert.Empty(sensor.Policies);

            var folder = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = _product, FolderId = Guid.NewGuid() }, default);
            Assert.True(folder.IsOk, folder.Error);

            var alerts = 0;
            void Record(AlertMessage message)
            {
                if (message.SensorId == sensor.Id)
                    Interlocked.Increment(ref alerts);
            }

            _valuesCache.NewAlertMessageEvent += Record;

            try
            {
                await UploadAsync(sensor, Content(Piece), name: "routed.bin");

                Assert.Equal(0, alerts);
            }
            finally
            {
                _valuesCache.NewAlertMessageEvent -= Record;
            }
        }

        [Fact]
        public async Task A_clock_stepped_back_below_a_clear_still_gets_the_file_a_row_above_the_floor_and_the_file_stays()
        {
            var sensor = await CreateFileSensorAsync("f");
            var t0 = DateTime.UtcNow;

            _valuesCache.LargeFileClock = () => t0;
            var first = await UploadAsync(sensor, Content(Piece), name: "first.dmp");

            // A clear up to just before it (HistoryClearedTo), then the clock steps back an hour.
            Assert.True((await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id, t0.AddMinutes(-1)))).IsOk);
            _valuesCache.LargeFileClock = () => t0.AddHours(-1);

            var second = await UploadAsync(sensor, Content(Piece), name: "second.dmp");

            // Above the floor of the clear (not pushed past the newest value: it is stored as a late value).
            Assert.Equal(t0.AddMinutes(-1).AddTicks(1), second.ValueTimeUtc);
            Assert.NotNull(_databaseCoreManager.DatabaseCore.GetSensorValue(sensor.Id, second.ValueTimeUtc.Value.Ticks));

            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);
            Assert.Equal(0, report.FilesRemoved);
            Assert.NotNull(Store.TryGetFile(second.FileId));
            Assert.Equal(2, (await ReadRowsAsync(sensor)).Count);
        }

        [Fact]
        public async Task A_store_that_cannot_be_read_refuses_a_file_sensors_move_to_another_product_and_lets_other_sensors_move()
        {
            if (!OperatingSystem.IsWindows())
                return; // the failure is made by holding a record without sharing, which Linux does not enforce

            var file = await CreateFileSensorAsync("f");
            var integer = await CreateIntSensorAsync("i");
            var other = await _valuesCache.AddProductAsync($"other{Guid.NewGuid():N}"[..14], Guid.Empty);

            var uploads = Path.Combine(Store.Folder, "uploads", _product.ToString("N"));
            Directory.CreateDirectory(uploads);
            var record = Path.Combine(uploads, $"{Guid.NewGuid():N}.rec");
            File.WriteAllText(record, "{}");

            using (File.Open(record, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var refused = await _valuesCache.MoveSensorAsync(file.Id, other.Id, "f");
                Assert.Equal(SensorMoveOutcome.NotSupported, refused.Outcome);
                Assert.Contains("cannot be read", refused.Error);
                Assert.Equal(SensorMoveOutcome.NotSupported, _valuesCache.PreviewMove(file.Id, other.Id, "f").Outcome);

                Assert.Equal(SensorMoveOutcome.Moved, (await _valuesCache.MoveSensorAsync(integer.Id, other.Id, "i")).Outcome);
            }
        }

        [Fact]
        public async Task A_fault_of_the_verified_record_is_counted_once()
        {
            if (!OperatingSystem.IsWindows())
                return; // the record write is made to fail by holding it without sharing

            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true);
            await AppendAllAsync(begin.Lease, content);

            var gate = new BlockingChecker(_checker);
            var finishing = _service.FinishAsync(begin.Lease, SHA256.HashData(content), gate, default);
            await gate.Entered.Task;

            var path = Path.Combine(Store.Folder, "uploads", _product.ToString("N"), $"{begin.FileId:N}.rec");

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                gate.Release.SetResult();
                await Assert.ThrowsAsync<LargeFileStoreException>(() => finishing);
            }

            Assert.Equal(1, Store.Statistics.WriteFailures);
            Assert.Equal(LargeFileState.Uploading, Store.TryGetFile(begin.FileId).State);
        }

        [Fact]
        public async Task The_status_and_the_sanitized_comment_of_the_begin_go_into_the_value_also_on_a_roll_forward()
        {
            var sensor = await CreateFileSensorAsync("f");
            var content = Content(Piece);

            await Assert.ThrowsAsync<ArgumentException>(() => _service.BeginAsync(Request(sensor.Path, 10) with { Comment = new string('x', 1025) }, default));

            var begin = await _service.BeginAsync(Request(sensor.Path, content.Length) with
            {
                Secure = false,
                Status = SensorStatus.Error,
                Comment = "crash 7<script>alert(1)</script>",
            }, default);
            Assert.False(begin.Secure);
            await AppendAllAsync(begin.Lease, content);
            var finish = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default);

            var record = Store.TryGetFile(finish.FileId);
            Assert.False(record.Secure);
            Assert.Null(record.PasswordVersion);
            Assert.Equal(SensorStatus.Error, record.Status);
            Assert.DoesNotContain("<script", record.Comment);
            Assert.StartsWith("crash 7", record.Comment);

            var value = Assert.IsType<LargeFileValue>(sensor.LastValue);
            Assert.Equal(SensorStatus.Error, value.Status);
            Assert.Equal(record.Comment, value.Comment);

            // Both kinds are verified files for the packer.
            Assert.Contains(Store.GetFiles(LargeFileState.Verified), r => r.FileId == finish.FileId);

            _databaseCoreManager.DatabaseCore.ClearSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue);
            Assert.Equal(1, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).RolledForward);

            var row = Assert.IsType<LargeFileValue>(Assert.Single(await ReadRowsAsync(sensor)));
            Assert.Equal(SensorStatus.Error, row.Status);
            Assert.Equal(record.Comment, row.Comment);
        }

        [Fact]
        public async Task A_file_whose_sensor_entity_cannot_be_read_back_is_kept_and_reported()
        {
            // The entity is in the database but cannot be deserialized: the tree does not build the sensor, and the list of
            // sensors leaves it out. It exists all the same.
            var entity = EntitiesFactory.BuildSensorEntity(name: "corrupt", parent: _product.ToString(), type: (byte)SensorType.File);
            _databaseCoreManager.DatabaseCore.AddSensor(entity);
            PutEnvironmentRow(entity.Id, System.Text.Encoding.UTF8.GetBytes("not an entity"));

            Assert.DoesNotContain(_databaseCoreManager.DatabaseCore.GetAllSensors(), s => s.Id == entity.Id);
            Assert.True(_databaseCoreManager.DatabaseCore.SensorEntityExists(Guid.Parse(entity.Id)));

            var fileId = VerifiedRecordOf(Guid.Parse(entity.Id));
            var report = await _service.RunPassAsync(DateTime.UtcNow, atStart: false);

            Assert.Equal(0, report.FilesRemoved);
            Assert.NotNull(Store.TryGetFile(fileId));
            Assert.Equal(1, Store.Statistics.FilesOfUnloadedSensors);
        }

        [Fact]
        public async Task A_large_value_takes_its_acceptance_time_even_after_a_value_a_year_ahead()
        {
            var sensor = await CreateFileSensorAsync("f");

            // A skewed clock wrote a value a year ahead.
            _valuesCache.LargeFileClock = () => DateTime.UtcNow.AddDays(365);
            await UploadAsync(sensor, Content(Piece), name: "future.dmp");

            var now = DateTime.UtcNow;
            _valuesCache.LargeFileClock = () => now;
            var finish = await UploadAsync(sensor, Content(Piece), name: "now.dmp");

            Assert.Equal(now, finish.ValueTimeUtc);
            Assert.NotNull(_databaseCoreManager.DatabaseCore.GetSensorValue(sensor.Id, now.Ticks));
            Assert.Equal(2, (await ReadRowsAsync(sensor)).Count);
        }

        [Fact]
        public async Task A_store_folder_lost_after_a_verified_file_still_gets_its_values_compared_at_start()
        {
            var root = Path.Combine(Path.GetTempPath(), $"hsm-lf-lost-{Guid.NewGuid():N}");
            var settings = new HSMDatabase.Settings.DatabaseSettings
            {
                DatabaseFolder = Path.Combine(root, "Databases"),
                LargeFilesFolder = Path.Combine(root, "LargeFiles"),
            };

            try
            {
                var first = new HSMDatabase.DatabaseWorkCore.DatabaseCore(settings);
                first.AddProduct(TestProductsManager.TestProduct);
                first.Dispose();

                var (database, cache) = Restart(settings);
                var service = new LargeFileUploadService(database, cache, new LargeFileOptions { MinFreeDiskMb = 0 });
                var path = $"{_root}/f";

                Assert.True((await cache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
                {
                    Update = new SensorUpdate { Id = Guid.Empty },
                    Type = SensorType.File,
                    TypeIsExplicit = true,
                })).IsOk);

                var content = Content(Piece);
                var begin = await service.BeginAsync(Request(path, content.Length), default);
                await service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default);
                Assert.Equal(LargeFileFinishOutcome.Verified, (await service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default)).Outcome);

                await cache.DisposeAsync();
                database.Dispose();

                // The volume of the store is lost, or replaced by an empty one; the databases stay.
                Directory.Delete(((HSMDatabase.AccessManager.IDatabaseSettings)settings).PathToLargeFiles, recursive: true);
                Directory.CreateDirectory(((HSMDatabase.AccessManager.IDatabaseSettings)settings).PathToLargeFiles);

                var (restarted, restartedCache) = Restart(database.DatabaseSettings);
                await using var _ = restartedCache;

                Assert.False(restarted.LargeFiles.HasContent);
                Assert.True(restarted.LargeFiles.WasUsed);

                var report = await new LargeFileUploadService(restarted, restartedCache, new LargeFileOptions { MinFreeDiskMb = 0 })
                    .RunPassAsync(DateTime.UtcNow, atStart: true);

                Assert.Equal(1, report.RowsWithoutRecord);
                restarted.Dispose();
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException) { }
            }
        }

        // A verified record of a sensor id, made through the store alone.
        private Guid VerifiedRecordOf(Guid sensorId)
        {
            var content = Content(Piece);
            var begin = Store.Begin(new LargeFileUploadKey(_product, sensorId, "x.dmp", content.Length, Modified, null), "dmp", Piece, Piece);
            Store.Append(begin.Lease, 0, content, SHA256.HashData(content));
            Store.StartFinish(begin.Lease);
            Store.MarkVerified(begin.FileId, SHA256.HashData(content), DateTime.UtcNow);
            Store.EndFinish(begin.Lease);

            return begin.FileId;
        }

        // Writes raw bytes under a key of the environment database (an entity that cannot be read back).
        private void PutEnvironmentRow(string key, byte[] value)
        {
            const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            var environment = _databaseCoreManager.DatabaseCore.GetType().GetField("_environmentDatabase", Private)!.GetValue(_databaseCoreManager.DatabaseCore);
            var adapter = environment!.GetType().GetField("_database", Private)!.GetValue(environment);
            adapter!.GetType().GetMethod("Put", [typeof(byte[]), typeof(byte[])])!.Invoke(adapter, [System.Text.Encoding.UTF8.GetBytes(key), value]);
        }

        [Fact]
        public async Task The_timeout_marker_of_a_large_last_value_is_a_plain_file_value_and_its_file_stays()
        {
            var sensor = await CreateFileSensorAsync("f");

            _valuesCache.LargeFileClock = () => DateTime.UtcNow.AddHours(-1);
            var verified = await UploadAsync(sensor, Content(Piece));
            _valuesCache.LargeFileClock = () => DateTime.UtcNow;

            // A TTL the last value has outlived: the next sweep step expires the sensor.
            sensor.Policies.AddTTLPolicy(new PolicyUpdate { TTL = TimeSpan.FromMinutes(1).Ticks });

            var marker = sensor.GetTimeoutValue();
            Assert.IsType<FileValue>(marker);
            Assert.True(marker.IsTimeout);

            // The cache's TTL sweep step writes the marker row.
            _valuesCache.RunSensorTimeoutStep(sensor);
            Assert.True(sensor.IsExpired);

            var rows = new List<byte[]>();
            await foreach (var row in _databaseCoreManager.DatabaseCore.GetSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue))
                rows.Add(row);

            Assert.Equal(2, rows.Count);
            Assert.Single(rows, r => LargeFileValue.IsLargeFileRow(r));
            Assert.True(Assert.IsType<FileValue>(new MemoryPackFormatter().Deserialize(rows.Single(r => !LargeFileValue.IsLargeFileRow(r)))).IsTimeout);

            Assert.Equal(0, (await _service.RunPassAsync(DateTime.UtcNow, atStart: false)).FilesRemoved);
            Assert.NotNull(Store.TryGetFile(verified.FileId));
        }

        [Fact]
        public async Task Files_are_kept_when_their_products_queue_does_not_answer_the_check()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));
            await SetKeepHistoryAsync(sensor, TimeSpan.FromDays(1));
            _databaseCoreManager.DatabaseCore.ClearSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue);

            _valuesCache.BeforeLargeFileCheck = _ => throw new InvalidOperationException("the queue does not answer (test)");
            var report = await _service.RunPassAsync(DateTime.UtcNow.AddDays(3), atStart: false);

            Assert.Equal(0, report.FilesRemoved);
            Assert.Equal(1, report.Unchecked);
            Assert.NotNull(Store.TryGetFile(verified.FileId));
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (var i = 0; i < 100 && !condition(); i++)
                await Task.Delay(50);
        }

        // === Move and merge ===

        [Fact]
        public async Task A_file_sensor_with_large_files_is_not_moved_to_another_product_nor_merged_but_moves_inside_its_product()
        {
            var sensor = await CreateFileSensorAsync("f");
            var occupant = await CreateFileSensorAsync("occupant");
            var plain = await CreateFileSensorAsync("plain");
            var other = await _valuesCache.AddProductAsync($"other{Guid.NewGuid():N}"[..14], Guid.Empty);

            // Nothing in the store: an ordinary move.
            Assert.Equal(SensorMoveOutcome.Moved, _valuesCache.PreviewMove(plain.Id, other.Id, "plain").Outcome);

            // A part counts already.
            var begin = await BeginAsync(sensor.Path, Piece, raw: true);
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);

            var across = await _valuesCache.MoveSensorAsync(sensor.Id, other.Id, "f");
            Assert.Equal(SensorMoveOutcome.NotSupported, across.Outcome);
            Assert.Contains("large files", across.Error);
            Assert.Equal(SensorMoveOutcome.NotSupported, _valuesCache.PreviewMove(sensor.Id, other.Id, "f").Outcome);

            var merged = await _valuesCache.MoveSensorAsync(sensor.Id, _product, occupant.Path, SensorMoveConflict.Merge);
            Assert.Equal(SensorMoveOutcome.NotSupported, merged.Outcome);
            Assert.Equal(SensorMoveOutcome.NotSupported, _valuesCache.PreviewMove(sensor.Id, _product, occupant.Path, SensorMoveConflict.Merge).Outcome);

            // The other side holding the files is refused too.
            var into = await _valuesCache.MoveSensorAsync(occupant.Id, _product, sensor.Path, SensorMoveConflict.Merge);
            Assert.Equal(SensorMoveOutcome.NotSupported, into.Outcome);

            // Inside the product: the id stays, the record follows.
            var inside = await _valuesCache.MoveSensorAsync(sensor.Id, _product, $"{_root}/moved");
            Assert.Equal(SensorMoveOutcome.Moved, inside.Outcome);
            Assert.Equal(sensor.Id, Store.TryGetFile(begin.FileId).SensorId);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, $"{_root}/moved", out var moved));
            Assert.Equal(sensor.Id, moved.Id);
        }

        // === The UI's value edit ===

        [Fact]
        public async Task A_new_value_from_the_UI_after_a_large_value_is_a_plain_file_value_and_a_change_of_the_last_keeps_the_large_one()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));

            var changed = await _valuesCache.UpdateSensorValueAsync(new UpdateSensorValueRequestModel(Guid.Empty, null)
            {
                Id = sensor.Id,
                Status = SensorStatus.Error,
                Comment = "looked at",
                ChangeLast = true,
            });
            Assert.True(changed.IsOk, changed.Error);

            var last = Assert.IsType<LargeFileValue>(sensor.LastValue);
            Assert.Equal(verified.FileId, last.FileId);
            Assert.Equal("looked at", last.Comment);

            var added = await _valuesCache.UpdateSensorValueAsync(new UpdateSensorValueRequestModel(Guid.Empty, null)
            {
                Id = sensor.Id,
                Status = SensorStatus.Ok,
                Comment = "new",
            });
            Assert.True(added.IsOk, added.Error);

            var plain = sensor.LastValue;
            Assert.IsNotType<LargeFileValue>(plain);
            Assert.IsType<FileValue>(plain);

            var rows = await ReadRowsAsync(sensor);
            Assert.Single(rows.OfType<LargeFileValue>());
            Assert.Equal(verified.FileId, rows.OfType<LargeFileValue>().Single().FileId);
        }


        // === Helpers ===

        private ILargeFileStore Store => _databaseCoreManager.DatabaseCore.LargeFiles;

        private LargeFileBeginRequest Request(string path, long size, string name = "dump.dmp") =>
            new(_product, path, name, size, Modified, "inode-1", Piece, Piece);

        private Task<LargeFileBeginResult> BeginAsync(string path, long size, bool raw = false, string name = "dump.dmp") =>
            _service.BeginAsync(Request(raw ? path : $"{_root}/{path}", size, name), default);

        private async Task AppendAllAsync(UploadLease lease, byte[] content, int from = 0, int? count = null)
        {
            var pieces = (content.Length + Piece - 1) / Piece;

            for (var i = from; i < (count ?? pieces); i++)
            {
                var piece = content.AsMemory(i * Piece, Math.Min(Piece, content.Length - i * Piece));
                var result = await _service.AppendAsync(lease, i, piece, SHA256.HashData(piece.Span), default);
                Assert.Equal(LargeFileAppendOutcome.Stored, result.Outcome);
            }
        }

        private async Task<LargeFileFinishResult> UploadAsync(BaseSensorModel sensor, byte[] content, string name = "dump.dmp")
        {
            var begin = await BeginAsync(sensor.Path, content.Length, raw: true, name: name);
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);

            await AppendAllAsync(begin.Lease, content);

            var finish = await _service.FinishAsync(begin.Lease, SHA256.HashData(content), _checker, default);
            Assert.Equal(LargeFileFinishOutcome.Verified, finish.Outcome);
            _service.Release(begin.Lease);

            return finish;
        }

        private async Task<List<BaseValue>> ReadRowsAsync(BaseSensorModel sensor)
        {
            var formatter = new MemoryPackFormatter();
            var rows = new List<BaseValue>();

            await foreach (var bytes in _databaseCoreManager.DatabaseCore.GetSensorValues(sensor.Id, DateTime.MinValue, DateTime.MaxValue))
                rows.Add(formatter.Deserialize(bytes));

            return rows;
        }

        private async Task SetKeepHistoryAsync(BaseSensorModel sensor, TimeSpan period) =>
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                KeepHistory = new TimeIntervalModel(period.Ticks),
                Initiator = InitiatorInfo.System,
            })).IsOk);

        private async Task<BaseSensorModel> CreateFileSensorAsync(string name, DefaultAlertsOptions defaultAlerts = DefaultAlertsOptions.None)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty, DefaultAlertsOptions = defaultAlerts },
                Type = SensorType.File,
                TypeIsExplicit = true,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            return sensor;
        }

        private async Task<BaseSensorModel> CreateIntSensorAsync(string name)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, $"{_root}/{name}", DateTime.UtcNow);
            Assert.True((await _valuesCache.AddSensorValueAsync(Guid.Parse(TestProductsManager.TestProductKey.Id), _product, value)).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, $"{_root}/{name}", out var sensor));
            return sensor;
        }

        private static byte[] Content(int length)
        {
            var bytes = new byte[length];
            new Random(length).NextBytes(bytes);
            return bytes;
        }

        // A restart of the server over the same environment database and folders (see TableBlockServiceTests.Restart).
        private static (HSMDatabase.DatabaseWorkCore.DatabaseCore Database, TreeValuesCache Cache) Restart(HSMDatabase.AccessManager.IDatabaseSettings settings)
        {
            var restart = ((HSMDatabase.Settings.DatabaseSettings)settings) with
            {
                ServerLayoutDatabaseName = settings.ServerLayoutDatabaseName + "_restart",
                ChangeSetsDatabaseName = settings.ChangeSetsDatabaseName + "_restart",
                SnaphotsDatabaseName = settings.SnaphotsDatabaseName + "_restart",
            };
            var database = new HSMDatabase.DatabaseWorkCore.DatabaseCore(restart);
            var snapshot = new Moq.Mock<HSMServer.Core.TreeStateSnapshot.ITreeStateSnapshot>();
            snapshot.Setup(s => s.Keys).Returns(new HSMServer.Core.TreeStateSnapshot.StateCollection<
                HSMServer.Core.TreeStateSnapshot.States.LastKeyState, HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity.LastKeyStateEntity>());

            var cache = new TreeValuesCache(database, snapshot.Object, new HSMServer.Core.Journal.JournalService(database),
                new HSMServer.Core.Schedule.AlertScheduleProvider(database), new HSMServer.Core.ChangeSets.ChangeSetJournal(database));

            return (database, cache);
        }


        // A record whose sensor is gone and that nothing removed (a removal that failed, a sensor removed at boot): the pass.
        [Fact]
        public async Task The_pass_removes_the_files_of_a_sensor_that_is_gone_when_its_removal_left_them()
        {
            var sensor = await CreateFileSensorAsync("f");
            var verified = await UploadAsync(sensor, Content(Piece));

            // The sensor goes without the cache's removal (as a sensor dropped at boot).
            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            _databaseCoreManager.DatabaseCore.RemoveSensorWithMetadata(sensor.Id);
            _databaseCoreManager.DatabaseCore.Dispose();

            var (database, cache) = Restart(settings);
            await using var _ = cache;
            var service = new LargeFileUploadService(database, cache, new LargeFileOptions { MinFreeDiskMb = 0 });

            var report = await service.RunPassAsync(DateTime.UtcNow, atStart: true);

            Assert.Equal(1, report.FilesRemoved);
            Assert.Null(database.LargeFiles.TryGetFile(verified.FileId));
        }


        private sealed class FixedChecker(LargeFilePartCheck answer) : ILargeFilePartChecker
        {
            public Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct) => Task.FromResult(answer);
        }

        private sealed class ThrowingChecker : ILargeFilePartChecker
        {
            public Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct) => throw new IOException("the part cannot be read");
        }

        // Holds a check until the test releases it.
        private sealed class BlockingChecker(ILargeFilePartChecker inner) : ILargeFilePartChecker
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
            {
                Entered.SetResult();
                await Release.Task;
                return await inner.CheckAsync(part, wholeHash, ct);
            }
        }
    }
}
