using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorInfoExtensions
    {
        private const char SensorPathSeparator = '/';


        public static SensorInfo Convert(this SensorValueBase sensorValue, string productName) =>
            new()
            {
                Path = sensorValue.Path,
                Description = sensorValue.Description,
                ProductName = productName,
                SensorType = GetSensorType(sensorValue),
                SensorName = ExtractSensor(sensorValue.Path),
            };


        private static SensorType GetSensorType(SensorValueBase sensorValue) =>
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

        private static string ExtractSensor(string path) => path?.Split(SensorPathSeparator)?[^1];
    }
}
