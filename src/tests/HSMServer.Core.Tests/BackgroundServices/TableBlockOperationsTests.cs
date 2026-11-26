using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBlocks;
using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableSensors;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

// #1844: the configuration section of table sensors and the self-monitoring of the block store.
public sealed class TableBlockOperationsTests : IDisposable
{
    private readonly Dictionary<string, object> _sensors = [];
    private readonly Mock<IDataCollector> _collector = new(MockBehavior.Strict);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-table-ops-{Guid.NewGuid():N}");
    private readonly TableBlockDatabase _store;


    public TableBlockOperationsTests()
    {
        _collector.Setup(c => c.CreateIntSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register<int>(path));
        _collector.Setup(c => c.CreateDoubleSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => Register<double>(path));

        _store = new TableBlockDatabase(_folder);
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
    public void The_configuration_defaults_are_the_block_format_limits_a_million_rows_and_a_gigabyte_free()
    {
        var options = new TableSensorsConfig().ToOptions();

        Assert.Equal(1_000_000, options.MaxReadRows);
        Assert.Equal(1024, options.MinFreeDiskMb);
        Assert.Equal(64 * 1024 * 1024, options.Limits.MaxDecompressedBytes);
        Assert.Equal(65_536, options.Limits.MaxTables);
        Assert.Equal(8 * 1024 * 1024, options.Limits.MaxRows);
        Assert.Equal(64, options.Limits.MaxColumnsPerTable);
        Assert.Equal(1024, options.Limits.MaxTableIdLength);
        Assert.Equal(32, options.MaxOpenBatchDatabases);
        Assert.Equal(24, options.DeleteMergedBlocksAfterHours);
        Assert.Equal(256, options.MergeMemoryBudgetMb);
        Assert.Equal(10, options.MoveSecondsPerPass);
        Assert.Equal(256, options.MoveMegabytesPerPass);
    }

    [Theory]
    [InlineData(nameof(TableSensorsConfig.MaxDecompressedBytes), 0)]
    [InlineData(nameof(TableSensorsConfig.MaxDecompressedBytes), (1 << 30) + 1)]
    [InlineData(nameof(TableSensorsConfig.MaxTables), 0)]
    [InlineData(nameof(TableSensorsConfig.MaxRows), -1)]
    [InlineData(nameof(TableSensorsConfig.MaxRows), (1 << 24) + 1)] // the read ranks a block's rows by (sequence << 24) | row
    [InlineData(nameof(TableSensorsConfig.MaxColumnsPerTable), 256)]
    [InlineData(nameof(TableSensorsConfig.MaxTableIdLength), 65_536)]
    [InlineData(nameof(TableSensorsConfig.MaxReadRows), 0)]
    [InlineData(nameof(TableSensorsConfig.MaxReadRows), int.MaxValue)] // one row past the limit is collected to page
    [InlineData(nameof(TableSensorsConfig.MinFreeDiskMb), -1)]
    [InlineData(nameof(TableSensorsConfig.MaxOpenBatchDatabases), 0)]
    [InlineData(nameof(TableSensorsConfig.DeleteMergedBlocksAfterHours), -1)]
    [InlineData(nameof(TableSensorsConfig.DeleteMergedBlocksAfterHours), 8761)]
    [InlineData(nameof(TableSensorsConfig.MergeMemoryBudgetMb), 0)]
    [InlineData(nameof(TableSensorsConfig.MoveSecondsPerPass), 0)]
    [InlineData(nameof(TableSensorsConfig.MoveSecondsPerPass), 3601)]
    [InlineData(nameof(TableSensorsConfig.MoveMegabytesPerPass), 0)]
    public void A_value_out_of_range_fails_the_validation_with_the_name_of_its_key(string key, long value)
    {
        var config = new TableSensorsConfig();
        var property = typeof(TableSensorsConfig).GetProperty(key);
        property.SetValue(config, property.PropertyType == typeof(long) ? value : (object)(int)value);

        var error = Assert.Throws<InvalidOperationException>(() => config.Validate());

        Assert.Contains($"TableSensors.{key} = {value}", error.Message);
    }

    [Fact]
    public void The_self_monitoring_posts_nothing_and_creates_no_sensor_until_the_store_is_opened()
    {
        var sensors = new TableBlockSensors(_collector.Object, _store);

        sensors.SendInfo();

        Assert.False(sensors.IsCreated);
        Assert.Empty(_sensors);
        Assert.False(Directory.Exists(_folder));

        _ = _store.NextSequence;
        sensors.SendInfo();

        Assert.True(sensors.IsCreated);
        Assert.Equal(0, Single<int>("Blocks stored").Value);
        Assert.Equal((0, SensorStatus.Ok), Single<int>("Write failures") is var w ? (w.Value, w.Status) : default);
    }

    [Fact]
    public void The_self_monitoring_posts_a_counter_when_it_changes_and_failures_as_errors()
    {
        var sensors = new TableBlockSensors(_collector.Object, _store);
        _ = _store.NextSequence;
        sensors.SendInfo();

        _store.Statistics.Add(ref _store.Statistics.BlocksStored, 3);
        _store.Statistics.Add(ref _store.Statistics.BytesStored, 2L << 20);
        _store.Statistics.Add(ref _store.Statistics.ReadFailures);
        sensors.SendInfo();
        sensors.SendInfo();

        Assert.Equal([0, 3], Values<int>("Blocks stored").Select(v => v.Value));
        Assert.Equal([0.0, 2.0], Values<double>("Stored").Select(v => v.Value));
        // A failure counter is Error in the sample in which it grew, and Ok again at the next sample without a new one.
        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error), (1, SensorStatus.Ok)],
            Values<int>("Read failures").Select(v => (v.Value, v.Status)));
        Assert.Single(Values<int>("Tables refused"));

        _store.Statistics.Add(ref _store.Statistics.WriteFailures);
        sensors.SendInfo();
        Assert.Equal((1, SensorStatus.Error), Values<int>("Write failures")[^1] is var w ? (w.Value, w.Status) : default);
        sensors.SendInfo();
        Assert.Equal((1, SensorStatus.Ok), Values<int>("Write failures")[^1] is var o ? (o.Value, o.Status) : default);
        sensors.SendInfo();
        Assert.Equal(3, Values<int>("Write failures").Count);
    }

    [Fact]
    public void The_batch_store_sensors_appear_once_a_product_of_it_is_opened_and_count_the_open_databases()
    {
        using var batches = new HSMDatabase.LevelDB.TableBatches.TableBatchDatabase(_folder);
        var sensors = new TableBlockSensors(_collector.Object, _store, batches);

        sensors.SendInfo();
        Assert.Empty(_sensors);

        batches.WriteHour(Guid.NewGuid(), new HSMDatabase.AccessManager.TableBatches.TableBatchHourCommit { HourTicks = new DateTime(2026, 11, 2).Ticks },
            [new HSMDatabase.AccessManager.TableBatches.TableBatchHourPart(4, [new(Guid.NewGuid(), 1, 2, 3, [1, 2, 3])])]);
        sensors.SendInfo();

        Assert.Equal(1, Single<int>("Batch databases open").Value);
        Assert.Equal(1, Single<int>("Batch week databases").Value);
        Assert.Equal(1, Single<int>("Merged hours").Value);
        Assert.False(sensors.IsCreated);
    }

    [Fact]
    public void The_merge_sensors_appear_with_the_batch_store_and_a_verification_mismatch_stays_an_error()
    {
        using var batches = new HSMDatabase.LevelDB.TableBatches.TableBatchDatabase(_folder);
        var sensors = new TableBlockSensors(_collector.Object, _store, batches);
        var merge = batches.MergeStatistics;

        merge.Add(ref merge.RowsMerged, 10);
        sensors.SendInfo();
        Assert.DoesNotContain(_sensors.Keys, k => k.EndsWith("/Rows merged"));

        batches.WriteHour(Guid.NewGuid(), new HSMDatabase.AccessManager.TableBatches.TableBatchHourCommit { HourTicks = new DateTime(2026, 11, 2).Ticks },
            [new HSMDatabase.AccessManager.TableBatches.TableBatchHourPart(4, [new(Guid.NewGuid(), 1, 2, 3, [1, 2, 3])])]);
        sensors.SendInfo();

        Assert.Equal(10, Single<int>("Rows merged").Value);
        Assert.Equal((0, SensorStatus.Ok), Single<int>("Verification mismatches") is var v ? (v.Value, v.Status) : default);
        foreach (var name in new[] { "Arrival hours merged", "Merge lag", "Last merge duration", "Unreadable blocks kept", "Rows of removed sensors dropped", "Rows expired before the merge", "Merged hours holding blocks", "Batch weeks dropped", "Retention faults", "Expired blocks deleted", "Expired block rows deleted", "Move weeks pending",
                                     "Move steps pending", "Move steps blocked", "Move steps refused now", "Moved rows", "Rows expired by the move", "Move faults", "Move steps refused", "Move leftovers cleaned", "Batch databases compacted", "Removals done", "Orphan table placements" })
            Assert.Equal(0, Single<int>(name).Value);
        Assert.Equal(0.0, Single<double>("Merged blocks size").Value);
        Assert.Equal(0.0, Single<double>("Merged batches size").Value);
        Assert.Equal(0.0, Single<double>("Moved batches size").Value);

        // A move fault is an error in the sample in which it grew, and Ok again at the next sample without a new one.
        merge.Add(ref merge.MoveFaults);
        sensors.SendInfo();
        sensors.SendInfo();
        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error), (1, SensorStatus.Ok)], Values<int>("Move faults").Select(x => (x.Value, x.Status)));

        // A mismatch is an error for as long as the count is above 0, not only in the sample in which it grew.
        merge.Add(ref merge.VerificationMismatches);
        merge.Set(ref merge.LagHours, 3);
        sensors.SendInfo();
        sensors.SendInfo();
        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error)], Values<int>("Verification mismatches").Select(x => (x.Value, x.Status)));
        Assert.Equal(3, Values<int>("Merge lag")[^1].Value);
    }

    [Fact]
    public void Duplicates_recognised_by_a_watermark_are_posted_with_the_block_store_counters()
    {
        var sensors = new TableBlockSensors(_collector.Object, _store);
        _ = _store.NextSequence;
        _store.Statistics.Add(ref _store.Statistics.MergedDuplicateBlocks, 2);
        sensors.SendInfo();

        Assert.Equal(2, Single<int>("Duplicates by watermark").Value);
    }

    [Fact]
    public void The_size_sensor_of_the_store_appears_once_the_store_exists()
    {
        var database = new Mock<IDatabaseCore>();
        database.Setup(d => d.TableBlocks).Returns(_store);
        database.Setup(d => d.TableBlocksDbSize).Returns(3L << 20);

        var sizes = new DatabaseSensorsSize(_collector.Object, database.Object, Mock.Of<IServerConfig>());
        sizes.SendInfo();
        Assert.DoesNotContain("Database/Tables data size", _sensors.Keys);

        _ = _store.NextSequence;
        sizes.SendInfo();

        Assert.Equal(3.0, Assert.Single(((Recording<double>)_sensors["Database/Tables data size"]).Values).Value);
    }


    private IInstantValueSensor<T> Register<T>(string path)
    {
        var sensor = new Recording<T>();
        _sensors[path] = sensor;
        return sensor;
    }

    private List<(T Value, SensorStatus Status, string Comment)> Values<T>(string name) => ((Recording<T>)_sensors[$"{TableBlockSensors.NodeName}/{name}"]).Values;

    private (T Value, SensorStatus Status, string Comment) Single<T>(string name) => Assert.Single(Values<T>(name));


    private sealed class Recording<T> : IInstantValueSensor<T>
    {
        public List<(T Value, SensorStatus Status, string Comment)> Values { get; } = [];

        public void AddValue(T value) => Values.Add((value, SensorStatus.Ok, string.Empty));

        public void AddValue(T value, string comment = "") => Values.Add((value, SensorStatus.Ok, comment));

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "") => Values.Add((value, status, comment));
    }
}
