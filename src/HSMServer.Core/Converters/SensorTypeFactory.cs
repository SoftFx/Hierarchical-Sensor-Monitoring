using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Converters
{
    public static class SensorTypeFactory
    {
        public static SensorType GetSensorType(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue => SensorType.BooleanSensor,
                IntSensorValue => SensorType.IntSensor,
                DoubleSensorValue => SensorType.DoubleSensor,
                StringSensorValue => SensorType.StringSensor,
                IntBarSensorValue => SensorType.IntegerBarSensor,
                DoubleBarSensorValue => SensorType.DoubleBarSensor,
                FileSensorBytesValue => SensorType.FileSensorBytes,
                FileSensorValue => SensorType.FileSensor,
                _ => (SensorType)0,
            };
    }
}
