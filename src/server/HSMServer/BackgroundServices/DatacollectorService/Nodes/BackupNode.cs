using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System;


namespace HSMServer.BackgroundServices;

public record BackupSensors
{
    private const string LocalBackupNode = "Local backup size";
    private const string RemoteBackupNode = "Remote backup size";
    private const string NodeName = "Backup";

    private IInstantValueSensor<double> _localBackupSensor { get; }
    private IInstantValueSensor<double> _remoteBackupSensor { get; }


    public BackupSensors(IDataCollector collector)
    {
        _localBackupSensor = collector.CreateDoubleSensor($"{NodeName}/{LocalBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,
            Description = $"The sensor sends information about {LocalBackupNode}. Contains backups of Environment and ServerLayout databases."
        });

        _remoteBackupSensor = collector.CreateDoubleSensor($"{NodeName}/{RemoteBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,
            Description = $"The sensor sends information about {RemoteBackupNode}. Contains backups of Environment and ServerLayout databases."
        });
    }

    public void AddLocalValue(long value, bool hasErrors, string message)
    {
        _localBackupSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value), GetStatus(hasErrors), message);
    }

    public void AddRemoteValue(long value, bool hasErrors, string message)
    {
        _remoteBackupSensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value), GetStatus(hasErrors), message);
    }


    private SensorStatus GetStatus(bool hasErrors) => hasErrors ? SensorStatus.Error : SensorStatus.Ok;
}
