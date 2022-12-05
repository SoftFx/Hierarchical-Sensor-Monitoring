using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System.Text.Json;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.ApiObjectsConverters
{
    public static class CommonSensorExtensions
    {
        public static SensorValueBase Convert(this CommonSensorValue value) =>
            value.SensorType switch
            {
                SensorType.IntegerBarSensor => value.Convert<IntBarSensorValue>(),
                SensorType.DoubleBarSensor => value.Convert<DoubleBarSensorValue>(),
                SensorType.DoubleSensor => value.Convert<DoubleSensorValue>(),
                SensorType.IntSensor => value.Convert<IntSensorValue>(),
                SensorType.BooleanSensor => value.Convert<BoolSensorValue>(),
                SensorType.StringSensor => value.Convert<StringSensorValue>(),
                _ => null,
            };

        public static T Convert<T>(this CommonSensorValue sensorValue) =>
            JsonSerializer.Deserialize<T>(sensorValue.TypedValue);
    }
}
