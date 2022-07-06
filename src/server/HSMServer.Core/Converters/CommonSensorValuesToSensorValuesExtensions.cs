using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class CommonSensorValuesToSensorValuesExtensions
    {
        public static BaseValue Convert(this CommonSensorValue value) =>
            value.SensorType switch
            {
                SensorType.IntegerBarSensor => value.Convert<IntBarSensorValue>().Convert(),
                SensorType.DoubleBarSensor => value.Convert<DoubleBarSensorValue>().Convert(),
                SensorType.DoubleSensor => value.Convert<DoubleSensorValue>().Convert(),
                SensorType.IntSensor => value.Convert<IntSensorValue>().Convert(),
                SensorType.BooleanSensor => value.Convert<BoolSensorValue>().Convert(),
                SensorType.StringSensor => value.Convert<StringSensorValue>().Convert(),
                _ => null,
            };

        internal static T Convert<T>(this CommonSensorValue sensorValue) =>
            JsonSerializer.Deserialize<T>(sensorValue.TypedValue);
    }
}
