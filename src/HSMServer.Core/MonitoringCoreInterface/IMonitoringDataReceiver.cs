using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface IMonitoringDataReceiver
    {
        void AddSensorValue<T>(T value) where T : SensorValueBase;
        void AddFileSensor(FileSensorBytesValue value);
    }
}