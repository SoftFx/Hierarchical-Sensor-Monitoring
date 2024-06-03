using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;


namespace HSMServer.BackgroundServices;

public record BackupSensors
{
    private const string CreateBackupNode = "Backup created";
    private const string UploadBackupNode = "Backup uploaded";
    private const string NodeName = "Backup";

    private readonly IInstantValueSensor<bool> _createBackupSensor;
    private readonly IInstantValueSensor<bool> _uploadBackupSensor;


    public BackupSensors(IDataCollector collector)
    {
        _createBackupSensor = collector.CreateBoolSensor($"{NodeName}/{CreateBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            EnableForGrafana = true,
            Description = "Database backup file created"
        });

        _uploadBackupSensor = collector.CreateBoolSensor($"{NodeName}/{UploadBackupNode}", new InstantSensorOptions
        {
            Alerts = [],
            EnableForGrafana = true,
            Description = "Database backup file uploaded"
        });

    }

    public void AddBackupCreateInfo(bool value, string message)
    {
        _createBackupSensor.AddValue(value, SetStatus(value) , message);
    }

    public void AddBackupUploadInfo(bool value, string message)
    {
        _uploadBackupSensor.AddValue(value, SetStatus(value), message);
    }

    private static SensorStatus SetStatus(bool value) => value ? SensorStatus.Ok : SensorStatus.Error;
}