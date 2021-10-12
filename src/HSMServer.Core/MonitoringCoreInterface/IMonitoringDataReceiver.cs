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
        void AddSensorValue(BoolSensorValue value);
        void AddSensorValue(IntSensorValue value);
        void AddSensorValue(DoubleSensorValue value);
        void AddSensorValue(StringSensorValue value);
        void AddSensorValue(IntBarSensorValue value);
        void AddSensorValue(DoubleBarSensorValue value);
        void AddSensorValue(FileSensorValue value);
        void AddSensorValue(FileSensorBytesValue value);
    }
}