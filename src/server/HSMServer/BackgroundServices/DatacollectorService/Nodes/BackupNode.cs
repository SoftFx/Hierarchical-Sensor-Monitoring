using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using NLog;
using System;


namespace HSMServer.BackgroundServices;

public record BackupSensors
{
    internal const string NodeName = "Backup";

    internal const string LocalBackupNode = "Local backup size";
    internal const string RemoteBackupNode = "Remote backup size";

    internal const string ResultSensorName = "Backup result";
    internal const string DurationSensorName = "Backup duration";
    internal const string EnvironmentBackupSizeName = "Environment backup size";
    internal const string DashboardsBackupSizeName = "Dashboards backup size";

    internal const string NotBackedUpNote = "Only the EnvironmentData (metadata) and ServerLayout (dashboards) databases are backed up; " +
                                            "the sensor values (History), Journals and Snapshots databases are not.";

    private static readonly TimeSpan MinBackupPeriod = TimeSpan.FromHours(1);

    private readonly Logger _logger = LogManager.GetLogger(nameof(BackupSensors));

    private IInstantValueSensor<double> _localBackupSensor { get; }
    private IInstantValueSensor<double> _remoteBackupSensor { get; }

    private readonly IInstantValueSensor<bool> _resultSensor;
    private readonly IInstantValueSensor<double> _durationSensor;
    private readonly IInstantValueSensor<double> _environmentBackupSizeSensor;
    private readonly IInstantValueSensor<double> _dashboardsBackupSizeSensor;


    public BackupSensors(IDataCollector collector, TimeSpan backupPeriod)
    {
        var period = NormalizePeriod(backupPeriod);
        var everyRun = $"Posted once per backup run (every {FormatPeriod(period)} by the current settings, and on a manual backup).";

        _localBackupSensor = collector.CreateDoubleSensor($"{NodeName}/{LocalBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.MB,
            Description = $"The sensor sends information about {LocalBackupNode}. Contains backups of Environment and ServerLayout databases."
        });

        _remoteBackupSensor = collector.CreateDoubleSensor($"{NodeName}/{RemoteBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.MB,
            Description = $"The sensor sends information about {RemoteBackupNode}. Contains backups of Environment and ServerLayout databases."
        });

        _resultSensor = collector.CreateBoolSensor($"{NodeName}/{ResultSensorName}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = GetResultTtl(backupPeriod),
            EnableForGrafana = true,
            Description = $"Outcome of the last local database backup: true and status Ok when every database was written, " +
                          $"false and status Error when any database backup failed (the comment carries the error; on success it lists the written files). " +
                          $"{everyRun} The TTL is the backup period plus half a period ({FormatPeriod(GetResultTtl(backupPeriod))}), " +
                          $"so a backup that stops running (service stuck, backups disabled) turns the sensor to Timeout. " +
                          $"The SFTP upload is reported by '{RemoteBackupNode}', not here. {NotBackedUpNote}"
        });

        _durationSensor = collector.CreateDoubleSensor($"{NodeName}/{DurationSensorName}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.Seconds,
            Description = $"Wall time of the local database backup in seconds: writing and zipping the EnvironmentData and ServerLayout backups, " +
                          $"including pruning old backup files; the SFTP upload is not included. {everyRun}"
        });

        _environmentBackupSizeSensor = collector.CreateDoubleSensor($"{NodeName}/{EnvironmentBackupSizeName}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.MB,
            Description = $"Size in MB of the backup file written by the last run for the EnvironmentData database " +
                          $"(folders, products, sensors, users, access keys): EnvironmentData_<time>.zip. " +
                          $"{everyRun} Not posted when that database's backup failed. {NotBackedUpNote}"
        });

        _dashboardsBackupSizeSensor = collector.CreateDoubleSensor($"{NodeName}/{DashboardsBackupSizeName}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.MB,
            Description = $"Size in MB of the backup file written by the last run for the ServerLayout database " +
                          $"(dashboards, panels, charts): ServerLayout_<time>.zip. " +
                          $"{everyRun} Not posted when that database's backup failed. {NotBackedUpNote}"
        });
    }


    // TTL of the result sensor: one backup period plus half a period of slack.
    // A single missed run turns the sensor to Timeout; the slack absorbs the
    // run's own duration and scheduling jitter. Periods below one hour are
    // treated as one hour (the configured unit is hours).
    internal static TimeSpan GetResultTtl(TimeSpan backupPeriod)
    {
        var period = NormalizePeriod(backupPeriod);

        return period + period / 2;
    }


    public void AddLocalValue(long value, bool hasErrors, string message) =>
        Post(LocalBackupNode, () => _localBackupSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value), GetStatus(hasErrors), message));

    public void AddRemoteValue(long value, bool hasErrors, string message) =>
        Post(RemoteBackupNode, () => _remoteBackupSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value), GetStatus(hasErrors), message));

    public void AddResult(bool isOk, string comment) =>
        Post(ResultSensorName, () => _resultSensor.AddValue(isOk, GetStatus(!isOk), comment ?? string.Empty));

    public void AddDuration(TimeSpan duration) =>
        Post(DurationSensorName, () => _durationSensor.AddValue(Math.Round(duration.TotalSeconds, 3, MidpointRounding.AwayFromZero)));

    public void AddEnvironmentBackupSize(long sizeInBytes, string fileName) =>
        Post(EnvironmentBackupSizeName, () => _environmentBackupSizeSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(sizeInBytes), fileName ?? string.Empty));

    public void AddDashboardsBackupSize(long sizeInBytes, string fileName) =>
        Post(DashboardsBackupSizeName, () => _dashboardsBackupSizeSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(sizeInBytes), fileName ?? string.Empty));


    // A failing self-monitoring post must never break or delay the backup.
    private void Post(string sensorName, Action post)
    {
        try
        {
            post();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Posting '{NodeName}/{sensorName}' failed: {ex.Message}");
        }
    }

    private static TimeSpan NormalizePeriod(TimeSpan backupPeriod) => backupPeriod < MinBackupPeriod ? MinBackupPeriod : backupPeriod;

    private static string FormatPeriod(TimeSpan period) => FormattableString.Invariant($"{period.TotalHours:0.#} h");

    private static SensorStatus GetStatus(bool hasErrors) => hasErrors ? SensorStatus.Error : SensorStatus.Ok;
}
