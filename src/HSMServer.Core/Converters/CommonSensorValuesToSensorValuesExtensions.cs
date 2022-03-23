using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class CommonSensorValuesToSensorValuesExtensions
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

        internal static T Convert<T>(this CommonSensorValue sensorValue) =>
            JsonSerializer.Deserialize<T>(sensorValue.TypedValue);
    }
}
