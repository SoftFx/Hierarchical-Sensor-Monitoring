using System;
using System.Collections.Generic;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface IMonitoringDataReceiver
    {
        [Obsolete("08.07.2021. Use void AddSensorsValues(IEnumerable<CommonSensorValue> values)")]
        void AddSensorsValues(List<CommonSensorValue> values);
        void AddSensorsValues(List<UnitedSensorValue> values);

        void AddSensorValue<T>(T value) where T : SensorValueBase;
        void AddFileSensor(FileSensorBytesValue value);
    }
}