using HSMSensorDataObjects.FullDataObject;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface IMonitoringDataReceiver
    {
        void AddSensorsValues(List<UnitedSensorValue> values);

        void AddSensorValue<T>(T value) where T : SensorValueBase;
        void AddFileSensor(FileSensorBytesValue value);
    }
}