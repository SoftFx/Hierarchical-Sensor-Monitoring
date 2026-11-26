using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

// #1883: the configuration section of large files and the self-monitoring of the large file store.
public sealed class LargeFileOperationsTests : IDisposable
{
    private readonly Dictionary<string, Recording> _sensors = [];
    private readonly Dictionary<string, List<double>> _doubles = [];
    private readonly Dictionary<string, InstantSensorOptions> _options = [];
    private readonly Mock<IDataCollector> _collector = new(MockBehavior.Strict);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-large-ops-{Guid.NewGuid():N}");
    private readonly LargeFileStore _store;


    public LargeFileOperationsTests()
    {
        _collector.Setup(c => c.CreateIntSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions options) =>
                  {
                      _options[path] = options;
                      return _sensors[path] = new Recording();
                  });

        _collector.Setup(c => c.CreateDoubleSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions options) =>
                  {
                      _options[path] = options;
                      var values = _doubles[path] = [];
                      var sensor = new Mock<IInstantValueSensor<double>>();
                      sensor.Setup(x => x.AddValue(It.IsAny<double>())).Callback((double v) => values.Add(v));
                      return sensor.Object;
                  });

        _store = new LargeFileStore(_folder) { FreeBytes = () => long.MaxValue / 2 };
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
    public void The_configuration_defaults_are_64_GiB_4_uploads_10_GiB_free_7_days_and_180_seconds_in_a_folder_next_to_the_databases()
    {
        var config = new LargeFilesConfig();
        var options = config.ToOptions();

        Assert.Equal("LargeFiles", config.Folder);
        Assert.Equal(64L * 1024 * 1024 * 1024, options.MaxFileSizeBytes);
        Assert.Equal(4, options.MaxUploadsInProgress);
        Assert.Equal(10_240, options.MinFreeDiskMb);
        Assert.Equal(7, options.AbandonedUploadDays);
        Assert.Equal(180, options.LeaseTimeoutSeconds);
        Assert.Equal(16, options.MaxUploadingPartsPerProduct);

        var limits = options.ToLimits();
        Assert.Equal(10_240L * 1024 * 1024, limits.MinFreeBytes);
        Assert.Equal(TimeSpan.FromDays(7), limits.AbandonedUploadAge);
        Assert.Equal(TimeSpan.FromSeconds(180), limits.LeaseTimeout);
        Assert.Equal(16, limits.MaxUploadingPartsPerProduct);
    }

    [Theory]
    [InlineData(nameof(LargeFilesConfig.MaxFileSizeBytes), 0)]
    [InlineData(nameof(LargeFilesConfig.MaxFileSizeBytes), (1L << 50) + 1)]
    [InlineData(nameof(LargeFilesConfig.MaxUploadsInProgress), 0)]
    [InlineData(nameof(LargeFilesConfig.MaxUploadsInProgress), 1025)]
    [InlineData(nameof(LargeFilesConfig.MinFreeDiskMb), -1)]
    [InlineData(nameof(LargeFilesConfig.AbandonedUploadDays), 0)]
    [InlineData(nameof(LargeFilesConfig.LeaseTimeoutSeconds), 9)]
    [InlineData(nameof(LargeFilesConfig.MaxUploadingPartsPerProduct), 0)]
    public void A_value_out_of_range_fails_the_validation_with_the_name_of_its_key(string key, long value)
    {
        var config = new LargeFilesConfig();
        var property = typeof(LargeFilesConfig).GetProperty(key);
        property.SetValue(config, property.PropertyType == typeof(long) ? value : (object)(int)value);

        var error = Assert.Throws<InvalidOperationException>(() => config.Validate());

        Assert.Contains($"LargeFiles.{key} = {value}", error.Message);
    }

    [Fact]
    public void An_empty_folder_fails_the_validation()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new LargeFilesConfig { Folder = " " }.Validate());

        Assert.Contains("LargeFiles.Folder", error.Message);
    }

    [Fact]
    public void The_self_monitoring_creates_no_sensor_until_the_store_has_a_folder_then_posts_counters_and_failures_as_errors()
    {
        var sensors = new LargeFileSensors(_collector.Object, _store);

        // The folder alone (a bind mount creates it on every server) is not a store that took a file.
        Directory.CreateDirectory(_folder);
        _store.EnsureOpened();
        sensors.SendInfo();

        Assert.False(sensors.IsCreated);
        Assert.Empty(_sensors);

        Assert.Equal(LargeFileBeginOutcome.Started,
            _store.Begin(new LargeFileUploadKey(Guid.NewGuid(), Guid.NewGuid(), "a", 10, DateTime.UtcNow, null), null, 65_536, 65_536).Outcome);
        sensors.SendInfo();

        Assert.True(sensors.IsCreated);
        Assert.Equal(13, _sensors.Count);
        Assert.Single(_doubles);
        Assert.All(_sensors.Keys.Concat(_doubles.Keys), k => Assert.StartsWith("Large files/", k));
        Assert.Equal((0, SensorStatus.Ok), Single("Refused for space"));

        _store.Statistics.Add(ref _store.Statistics.RefusedForSpace);
        _store.Statistics.Add(ref _store.Statistics.FilesRemoved, 2);
        sensors.SendInfo();
        sensors.SendInfo();

        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error), (1, SensorStatus.Ok)], Values("Refused for space"));
        Assert.Equal([(0, SensorStatus.Ok), (2, SensorStatus.Ok)], Values("Files removed"));
    }


    // #1886: the gauges after a refusal and after a removal, on the real store.
    [Fact]
    public void After_a_refusal_the_counter_warns_with_its_alert_and_the_gauges_show_what_is_stored()
    {
        var sensors = new LargeFileSensors(_collector.Object, _store);
        var product = Guid.NewGuid();

        var started = _store.Begin(new LargeFileUploadKey(product, Guid.NewGuid(), "a.dmp", 10, DateTime.UtcNow, null), null, 65_536, 65_536);
        Assert.Equal(LargeFileBeginOutcome.Started, started.Outcome);
        Append(started.Lease, 0, 7);

        sensors.SendInfo();

        Assert.Equal((1, SensorStatus.Ok), Single("Uploads in progress"));
        Assert.Equal((0, SensorStatus.Ok), Single("Files stored"));
        Assert.Equal((0, SensorStatus.Ok), Single("Refused for space"));

        _store.FreeBytes = () => 0;
        Assert.Equal(LargeFileBeginOutcome.NoRoom,
            _store.Begin(new LargeFileUploadKey(product, Guid.NewGuid(), "b.dmp", 1L << 30, DateTime.UtcNow, null), null, 65_536, 65_536).Outcome);

        sensors.SendInfo();

        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error)], Values("Refused for space"));
        Assert.Equal((1, SensorStatus.Ok), Single("Uploads in progress"));

        // The one counter that warns: an alert when it turns Error, whose text never prints an empty comment.
        var alert = Assert.Single(_options[$"{LargeFileSensors.NodeName}/{LargeFileSensors.RefusedForSpaceName}"].Alerts);
        Assert.Equal(LargeFileSensors.RefusedForSpaceAlert, alert.Template);
        Assert.DoesNotContain("$comment", alert.Template);
        Assert.Empty(_options[$"{LargeFileSensors.NodeName}/Files removed"].Alerts);
    }

    [Fact]
    public void After_a_removal_the_counter_grows_and_the_gauges_drop()
    {
        var sensors = new LargeFileSensors(_collector.Object, _store);

        var started = _store.Begin(new LargeFileUploadKey(Guid.NewGuid(), Guid.NewGuid(), "a.dmp", 10, DateTime.UtcNow, null), null, 65_536, 65_536);
        Append(started.Lease, 0, 65_536);
        sensors.SendInfo();

        Assert.Equal(0.06, Last("Bytes stored"));

        _store.Discard(started.Lease, "test");
        sensors.SendInfo();

        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Ok)], Values("Parts removed"));
        Assert.Equal([(1, SensorStatus.Ok), (0, SensorStatus.Ok)], Values("Uploads in progress"));
        Assert.Equal(0, Last("Bytes stored"));
    }

    [Fact]
    public void Only_a_secure_verified_file_of_a_product_without_a_password_waits_and_a_packed_file_counts_its_archive()
    {
        var withPassword = Guid.NewGuid();
        var without = Guid.NewGuid();

        LargeFileRecord Record(LargeFileState state, Guid product, bool secure = true) => new()
        {
            FileId = Guid.NewGuid(),
            ProductId = product,
            State = state,
            Secure = secure,
            PartLength = 100,
            ArchiveLength = state == LargeFileState.Packed ? 1000 : null,
        };

        var gauges = LargeFileSensors.Measure(
        [
            Record(LargeFileState.Uploading, without),
            Record(LargeFileState.Verified, without),
            Record(LargeFileState.Verified, without, secure: false),
            Record(LargeFileState.Verified, withPassword),
            Record(LargeFileState.Packed, without, secure: false),
        ], product => product == withPassword);

        Assert.Equal(new LargeFileSensors.Gauges(UploadsInProgress: 1, FilesStored: 4, BytesStored: 100 * 4 + 1000, WaitingForPassword: 1), gauges);
    }

    [Fact]
    public void The_failed_builds_counter_reads_the_archive_side_once_it_is_handed_over_and_warns_when_it_grows()
    {
        var sensors = new LargeFileSensors(_collector.Object, _store);
        long failed = 0;

        Assert.Equal(LargeFileBeginOutcome.Started,
            _store.Begin(new LargeFileUploadKey(Guid.NewGuid(), Guid.NewGuid(), "a", 10, DateTime.UtcNow, null), null, 65_536, 65_536).Outcome);

        // Before the archive side is built: 0.
        sensors.SendInfo();

        sensors.UseArchiveSide(() => failed, _ => true);
        failed = 2;
        sensors.SendInfo();

        Assert.Equal([(0, SensorStatus.Ok), (2, SensorStatus.Error)], Values("Archive builds failed"));
    }

    [Fact]
    public void The_archive_side_is_asked_at_each_call_and_absent_services_count_as_nothing_failed_and_a_password_set()
    {
        var builder = new Mock<HSMServer.Core.LargeFiles.Archives.IArchiveBuilder>();
        var statistics = new HSMServer.Core.LargeFiles.Archives.ArchiveBuildStatistics { Failed = 3 };
        builder.SetupGet(b => b.Statistics).Returns(statistics);

        var passwords = new Mock<HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService>();
        var withPassword = Guid.NewGuid();
        passwords.Setup(p => p.HasPassword(withPassword)).Returns(true);

        var empty = new LargeFileArchiveSide(new Mock<IServiceProvider>().Object);
        Assert.Equal(0, empty.FailedBuilds());
        Assert.True(empty.HasDumpPassword(Guid.NewGuid()));

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(HSMServer.Core.LargeFiles.Archives.IArchiveBuilder))).Returns(builder.Object);
        provider.Setup(p => p.GetService(typeof(HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService))).Returns(passwords.Object);

        var side = new LargeFileArchiveSide(provider.Object);

        // Nothing is resolved when it is made: only when asked.
        provider.Verify(p => p.GetService(It.IsAny<Type>()), Times.Never());

        Assert.Equal(3, side.FailedBuilds());
        Assert.True(side.HasDumpPassword(withPassword));
        Assert.False(side.HasDumpPassword(Guid.NewGuid()));
    }

    [Fact]
    public void A_store_that_cannot_read_its_records_skips_the_gauges_but_not_the_counters()
    {
        var store = new Mock<ILargeFileStore>();
        store.SetupGet(s => s.HasContent).Returns(true);
        store.SetupGet(s => s.Statistics).Returns(new LargeFileStoreStatistics());
        store.Setup(s => s.GetFiles()).Throws(new LargeFileStoreException("disk gone"));

        var sensors = new LargeFileSensors(_collector.Object, store.Object);

        sensors.SendInfo();

        Assert.Equal((0, SensorStatus.Ok), Single("Refused for space"));
        Assert.Empty(Values("Uploads in progress"));
        Assert.Empty(_doubles[$"{LargeFileSensors.NodeName}/Bytes stored"]);
    }


    private void Append(UploadLease lease, long piece, int length)
    {
        var bytes = new byte[length];
        Assert.Equal(LargeFileAppendOutcome.Stored, _store.Append(lease, piece, bytes, new byte[32]).Outcome);
    }

    private double Last(string name) => _doubles[$"{LargeFileSensors.NodeName}/{name}"][^1];


    private List<(int Value, SensorStatus Status)> Values(string name) => _sensors[$"{LargeFileSensors.NodeName}/{name}"].Values;

    private (int Value, SensorStatus Status) Single(string name) => Assert.Single(Values(name));


    private sealed class Recording : IInstantValueSensor<int>
    {
        public List<(int Value, SensorStatus Status)> Values { get; } = [];

        public void AddValue(int value) => Values.Add((value, SensorStatus.Ok));

        public void AddValue(int value, string comment = "") => Values.Add((value, SensorStatus.Ok));

        public void AddValue(int value, SensorStatus status = SensorStatus.Ok, string comment = "") => Values.Add((value, status));
    }
}
