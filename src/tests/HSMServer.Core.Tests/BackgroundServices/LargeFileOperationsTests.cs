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
    private readonly Mock<IDataCollector> _collector = new(MockBehavior.Strict);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-large-ops-{Guid.NewGuid():N}");
    private readonly LargeFileStore _store;


    public LargeFileOperationsTests()
    {
        _collector.Setup(c => c.CreateIntSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                  .Returns((string path, InstantSensorOptions _) => _sensors[path] = new Recording());

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

        _store.EnsureOpened();
        sensors.SendInfo();

        Assert.False(sensors.IsCreated);
        Assert.Empty(_sensors);

        Assert.Equal(LargeFileBeginOutcome.Started,
            _store.Begin(new LargeFileUploadKey(Guid.NewGuid(), Guid.NewGuid(), "a", 10, DateTime.UtcNow, null), null, 65_536, 65_536).Outcome);
        sensors.SendInfo();

        Assert.True(sensors.IsCreated);
        Assert.Equal(8, _sensors.Count);
        Assert.All(_sensors.Keys, k => Assert.StartsWith("Database/Large files/", k));
        Assert.Equal((0, SensorStatus.Ok), Single("Refused for space"));

        _store.Statistics.Add(ref _store.Statistics.RefusedForSpace);
        _store.Statistics.Add(ref _store.Statistics.FilesRemoved, 2);
        sensors.SendInfo();
        sensors.SendInfo();

        Assert.Equal([(0, SensorStatus.Ok), (1, SensorStatus.Error), (1, SensorStatus.Ok)], Values("Refused for space"));
        Assert.Equal([(0, SensorStatus.Ok), (2, SensorStatus.Ok)], Values("Files removed"));
    }


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
