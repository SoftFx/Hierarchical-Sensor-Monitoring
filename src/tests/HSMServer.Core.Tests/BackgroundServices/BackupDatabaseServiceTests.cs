using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMDatabase.Settings;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.BackgroundServices;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

/// <summary>
/// Server self-monitoring of the database backup (#1525): what BackupDatabaseService posts per run
/// (result, per-file size, duration), the registration shape of the Backup node sensors and of the
/// per-database size sensors, and the isolation rule — a failing post never breaks the backup.
/// The collector is faked: every created sensor is a recording double keyed by its path.
/// </summary>
public sealed class BackupDatabaseServiceTests : IDisposable
{
    private const double MbBytes = 1 << 20;

    private readonly string _backupsFolder = Path.Combine(Path.GetTempPath(), $"hsm-backup-tests-{Guid.NewGuid():N}");

    private readonly FakeCollector _collector = new();
    private readonly Mock<IDatabaseCore> _database = new();
    private readonly Mock<IDashboardCollection> _dashboards = new();
    private readonly BackupDatabaseConfig _backupConfig = new() { IsEnabled = true, PeriodHours = 24 };
    private readonly IDatabaseSettings _dbSettings;


    public BackupDatabaseServiceTests()
    {
        Directory.CreateDirectory(_backupsFolder);
        _dbSettings = new DatabaseSettings { DatabaseBackupsFolder = _backupsFolder };

        _database.SetupGet(d => d.Dashboards).Returns(_dashboards.Object);
        _database.SetupGet(d => d.BackupsSize).Returns(0);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_backupsFolder, true);
        }
        catch
        {
            // best-effort cleanup of the temp folder
        }
    }


    private sealed class TestableBackupDatabaseService(IDatabaseCore database, IServerConfig config, BackupSensors sensors, IDatabaseSettings settings)
        : BackupDatabaseService(database, config, sensors, settings)
    {
        public Task Run() => ServiceActionAsync(CancellationToken.None);
    }

    private TestableBackupDatabaseService CreateService()
    {
        var config = new Mock<IServerConfig>();
        config.SetupGet(c => c.BackupDatabase).Returns(_backupConfig);

        var sensors = new BackupSensors(_collector.Object, TimeSpan.FromHours(_backupConfig.PeriodHours));

        return new TestableBackupDatabaseService(_database.Object, config.Object, sensors, _dbSettings);
    }

    // A backup action that behaves like the LevelDB one: writes <path>.zip and returns its full path.
    private static Func<string, TaskResult<string>> WritesZip(int sizeBytes) => path =>
    {
        var file = $"{path}.zip";
        File.WriteAllBytes(file, new byte[sizeBytes]);
        return TaskResult<string>.FromValue(Path.GetFullPath(file));
    };

    private void SetupEnvironmentBackup(Func<string, TaskResult<string>> action) =>
        _database.Setup(d => d.BackupEnvironment(It.IsAny<string>())).Returns(action);

    private void SetupDashboardsBackup(Func<string, TaskResult<string>> action) =>
        _dashboards.Setup(d => d.Backup(It.IsAny<string>())).Returns(action);

    private RecordingSensor<T> Sensor<T>(string name) => (RecordingSensor<T>)_collector.Sensors[$"{BackupSensors.NodeName}/{name}"];


    [Fact]
    public async Task Success_PostsOkResultSizesAndDuration()
    {
        SetupEnvironmentBackup(WritesZip(3 * (1 << 20)));
        SetupDashboardsBackup(WritesZip(1 << 19));

        await CreateService().Run();

        var result = Assert.Single(Sensor<bool>(BackupSensors.ResultSensorName).Values);
        Assert.True(result.Value);
        Assert.Equal(SensorStatus.Ok, result.Status);
        Assert.Contains($"{_dbSettings.EnvironmentDatabaseName}_", result.Comment);
        Assert.Contains($"{_dbSettings.ServerLayoutDatabaseName}_", result.Comment);

        var environmentSize = Assert.Single(Sensor<double>(BackupSensors.EnvironmentBackupSizeName).Values);
        Assert.Equal(3.0, environmentSize.Value);
        Assert.StartsWith($"{_dbSettings.EnvironmentDatabaseName}_", environmentSize.Comment);
        Assert.EndsWith(".zip", environmentSize.Comment);

        var dashboardsSize = Assert.Single(Sensor<double>(BackupSensors.DashboardsBackupSizeName).Values);
        Assert.Equal(0.5, dashboardsSize.Value);
        Assert.StartsWith($"{_dbSettings.ServerLayoutDatabaseName}_", dashboardsSize.Comment);

        var duration = Assert.Single(Sensor<double>(BackupSensors.DurationSensorName).Values);
        Assert.True(duration.Value >= 0);

        var local = Assert.Single(Sensor<double>(BackupSensors.LocalBackupNode).Values);
        Assert.Equal(SensorStatus.Ok, local.Status);
    }

    [Fact]
    public async Task Success_LocalBackupComment_ListsBothWrittenFiles()
    {
        SetupEnvironmentBackup(WritesZip(16));
        SetupDashboardsBackup(WritesZip(16));

        await CreateService().Run();

        // Regression: the dashboards branch used to append the environment file twice.
        var comment = Assert.Single(Sensor<double>(BackupSensors.LocalBackupNode).Values).Comment;
        Assert.Contains($"{_dbSettings.EnvironmentDatabaseName}_", comment);
        Assert.Contains($"{_dbSettings.ServerLayoutDatabaseName}_", comment);
    }

    [Fact]
    public async Task FailedDatabaseBackup_PostsErrorWithTheFirstErrorLine_AndSkipsThatSize()
    {
        SetupEnvironmentBackup(_ => TaskResult<string>.FromError(
            "Backup database X error: System.IO.IOException: There is not enough space on the disk.\r\n   at HSMDatabase.LevelDB.Database.Backup(String backupPath)"));
        SetupDashboardsBackup(WritesZip(1 << 20));

        await CreateService().Run();

        var result = Assert.Single(Sensor<bool>(BackupSensors.ResultSensorName).Values);
        Assert.False(result.Value);
        Assert.Equal(SensorStatus.Error, result.Status);
        Assert.Equal("Backup database X error: System.IO.IOException: There is not enough space on the disk.", result.Comment);

        Assert.Empty(Sensor<double>(BackupSensors.EnvironmentBackupSizeName).Values);
        Assert.Equal(1.0, Assert.Single(Sensor<double>(BackupSensors.DashboardsBackupSizeName).Values).Value);
        Assert.Single(Sensor<double>(BackupSensors.DurationSensorName).Values);
        Assert.Equal(SensorStatus.Error, Assert.Single(Sensor<double>(BackupSensors.LocalBackupNode).Values).Status);
    }

    [Fact]
    public async Task ThrowingDatabaseBackup_PostsErrorWithTheExceptionMessage()
    {
        // Before #1525 a throwing backup action returned null and the run died on a NullReferenceException.
        SetupEnvironmentBackup(WritesZip(16));
        SetupDashboardsBackup(_ => throw new InvalidOperationException("layout db is locked"));

        await CreateService().Run();

        var result = Assert.Single(Sensor<bool>(BackupSensors.ResultSensorName).Values);
        Assert.False(result.Value);
        Assert.Equal(SensorStatus.Error, result.Status);
        Assert.Contains("layout db is locked", result.Comment);
        Assert.StartsWith(_dbSettings.ServerLayoutDatabaseName, result.Comment);

        Assert.Single(Sensor<double>(BackupSensors.EnvironmentBackupSizeName).Values);
        Assert.Empty(Sensor<double>(BackupSensors.DashboardsBackupSizeName).Values);
    }

    [Fact]
    public async Task FailingSensorPosts_NeverBreakTheBackup()
    {
        var environmentCalls = 0;
        var dashboardsCalls = 0;
        SetupEnvironmentBackup(path => { environmentCalls++; return WritesZip(16)(path); });
        SetupDashboardsBackup(path => { dashboardsCalls++; return WritesZip(16)(path); });

        var service = CreateService();

        foreach (var sensor in _collector.Sensors.Values)
            sensor.Throws = true;

        await service.Run(); // must not throw

        Assert.Equal(1, environmentCalls);
        Assert.Equal(1, dashboardsCalls);
        Assert.Equal(2, Directory.GetFiles(_backupsFolder, "*.zip").Length);

        // Every per-run post was attempted (one throwing post does not skip the next).
        foreach (var name in new[] { BackupSensors.LocalBackupNode, BackupSensors.EnvironmentBackupSizeName, BackupSensors.DashboardsBackupSizeName, BackupSensors.DurationSensorName })
            Assert.Equal(1, Sensor<double>(name).Attempts);

        Assert.Equal(1, Sensor<bool>(BackupSensors.ResultSensorName).Attempts);
    }

    [Fact]
    public async Task DisabledBackup_PostsNothing()
    {
        _backupConfig.IsEnabled = false;

        await CreateService().Run();

        _database.Verify(d => d.BackupEnvironment(It.IsAny<string>()), Times.Never);
        Assert.All(_collector.Sensors.Values, s => Assert.Equal(0, s.Attempts));
    }


    [Theory]
    [InlineData(24, 36 * 60)]
    [InlineData(1, 90)]
    [InlineData(6, 9 * 60)]
    [InlineData(0, 90)] // a non-positive period is treated as one hour
    public void ResultTtl_IsThePeriodPlusHalfAPeriod(int periodHours, int expectedTtlMinutes)
    {
        _ = new BackupSensors(_collector.Object, TimeSpan.FromHours(periodHours));

        var options = _collector.Options[$"{BackupSensors.NodeName}/{BackupSensors.ResultSensorName}"];

        Assert.Equal(TimeSpan.FromMinutes(expectedTtlMinutes), options.TTL);
        Assert.Equal(TimeSpan.FromMinutes(expectedTtlMinutes), BackupSensors.GetResultTtl(TimeSpan.FromHours(periodHours)));
    }

    [Fact]
    public void Registration_PinsTypesUnitsTtlsAndDescriptions()
    {
        _ = new BackupSensors(_collector.Object, TimeSpan.FromHours(24));

        InstantSensorOptions Options(string name) => _collector.Options[$"{BackupSensors.NodeName}/{name}"];

        var result = Options(BackupSensors.ResultSensorName);
        Assert.IsType<RecordingSensor<bool>>(_collector.Sensors[$"{BackupSensors.NodeName}/{BackupSensors.ResultSensorName}"]);
        Assert.Null(result.SensorUnit);
        Assert.Empty(result.Alerts);
        Assert.Contains("once per backup run", result.Description);
        Assert.Contains("every 24 h", result.Description);
        Assert.Contains("Timeout", result.Description);
        Assert.Contains("36 h", result.Description);
        Assert.Contains("are not", result.Description);

        var duration = Options(BackupSensors.DurationSensorName);
        Assert.Equal(Unit.Seconds, duration.SensorUnit);
        Assert.Equal(TimeSpan.MaxValue, duration.TTL);
        Assert.Contains("seconds", duration.Description);
        Assert.Contains("once per backup run", duration.Description);

        foreach (var name in new[] { BackupSensors.EnvironmentBackupSizeName, BackupSensors.DashboardsBackupSizeName })
        {
            var size = Options(name);
            Assert.Equal(Unit.MB, size.SensorUnit);
            Assert.Equal(TimeSpan.MaxValue, size.TTL);
            Assert.Empty(size.Alerts);
            Assert.Contains("MB", size.Description);
            Assert.Contains("once per backup run", size.Description);
            Assert.Contains(BackupSensors.NotBackedUpNote, size.Description);
        }

        // The pre-existing sensors keep their paths.
        Assert.Contains($"{BackupSensors.NodeName}/{BackupSensors.LocalBackupNode}", _collector.Options.Keys);
        Assert.Contains($"{BackupSensors.NodeName}/{BackupSensors.RemoteBackupNode}", _collector.Options.Keys);
    }

    [Fact]
    public void DatabaseSizes_RegisterEnvironmentAndDashboards_AndPostTheirSizesInMb()
    {
        var database = new Mock<IDatabaseCore>();
        database.SetupGet(d => d.EnvironmentDbSize).Returns(5L << 20);
        database.SetupGet(d => d.ServerLayoutDbSize).Returns(1L << 19);

        var sizes = new DatabaseSensorsSize(_collector.Object, database.Object, Mock.Of<IServerConfig>());
        sizes.SendInfo();

        static string PathOf(string db) => $"Database/{db} data size";

        foreach (var db in new[] { DatabaseSensorsSize.EnvironmentDbName, DatabaseSensorsSize.DashboardsDbName, DatabaseSensorsSize.HistoryDbName })
        {
            var options = _collector.Options[PathOf(db)];
            Assert.Equal(Unit.MB, options.SensorUnit);
            Assert.Contains("in MB, posted once a day", options.Description);
        }

        Assert.Contains(BackupSensors.NotBackedUpNote, _collector.Options[PathOf(DatabaseSensorsSize.HistoryDbName)].Description);

        Assert.Equal(5.0, Assert.Single(((RecordingSensor<double>)_collector.Sensors[PathOf(DatabaseSensorsSize.EnvironmentDbName)]).Values).Value);
        Assert.Equal(0.5, Assert.Single(((RecordingSensor<double>)_collector.Sensors[PathOf(DatabaseSensorsSize.DashboardsDbName)]).Values).Value);
    }


    internal abstract class RecordingSensorBase
    {
        public bool Throws { get; set; }

        public int Attempts { get; protected set; }
    }

    internal sealed class RecordingSensor<T> : RecordingSensorBase, IInstantValueSensor<T>
    {
        public List<(T Value, SensorStatus Status, string Comment)> Values { get; } = [];

        public void AddValue(T value) => Record(value, SensorStatus.Ok, string.Empty);

        public void AddValue(T value, string comment = "") => Record(value, SensorStatus.Ok, comment);

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "") => Record(value, status, comment);

        private void Record(T value, SensorStatus status, string comment)
        {
            Attempts++;

            if (Throws)
                throw new InvalidOperationException("collector is broken");

            Values.Add((value, status, comment));
        }
    }

    // Only the two factories the backup and database-size sensors use are set up;
    // any other IDataCollector call fails the test loudly (strict mock).
    internal sealed class FakeCollector
    {
        private readonly Mock<IDataCollector> _mock = new(MockBehavior.Strict);

        public Dictionary<string, RecordingSensorBase> Sensors { get; } = [];

        public Dictionary<string, InstantSensorOptions> Options { get; } = [];

        public IDataCollector Object => _mock.Object;


        public FakeCollector()
        {
            _mock.Setup(c => c.CreateDoubleSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                 .Returns((string path, InstantSensorOptions options) => Register(new RecordingSensor<double>(), path, options));

            _mock.Setup(c => c.CreateBoolSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                 .Returns((string path, InstantSensorOptions options) => Register(new RecordingSensor<bool>(), path, options));
        }


        private RecordingSensor<T> Register<T>(RecordingSensor<T> sensor, string path, InstantSensorOptions options)
        {
            Sensors[path] = sensor;
            Options[path] = options;

            return sensor;
        }
    }
}
