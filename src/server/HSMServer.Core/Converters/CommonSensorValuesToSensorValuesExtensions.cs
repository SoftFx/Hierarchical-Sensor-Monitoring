using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class CommonSensorValuesToSensorValuesExtensions
    {
        public static BaseValue ConvertToValue(this CommonSensorValue value) =>
            value.SensorType switch
            {
                SensorType.IntegerBarSensor => value.Convert<IntBarSensorValue>().ConvertToValue(),
                SensorType.DoubleBarSensor => value.Convert<DoubleBarSensorValue>().ConvertToValue(),
                SensorType.DoubleSensor => value.Convert<DoubleSensorValue>().ConvertToValue(),
                SensorType.IntSensor => value.Convert<IntSensorValue>().ConvertToValue(),
                SensorType.BooleanSensor => value.Convert<BoolSensorValue>().ConvertToValue(),
                SensorType.StringSensor => value.Convert<StringSensorValue>().ConvertToValue(),
                _ => null,
            };

        internal static T Convert<T>(this CommonSensorValue sensorValue) =>
            JsonSerializer.Deserialize<T>(sensorValue.TypedValue);
    }
}
