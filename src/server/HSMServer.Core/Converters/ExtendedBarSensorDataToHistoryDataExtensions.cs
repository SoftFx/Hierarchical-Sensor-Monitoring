using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Core.Converters
{
    public static class ExtendedBarSensorDataToHistoryDataExtensions
    {
        public static SensorHistoryData Convert(this ExtendedBarSensorData data) =>
            data.ValueType switch
            {
                SensorType.IntegerBarSensor => CreateSensorHistoryData(data.Value),
                SensorType.DoubleBarSensor => CreateSensorHistoryData(data.Value),
                _ => null,
            };


        private static SensorHistoryData CreateSensorHistoryData(BarSensorValueBase sensorValue) =>
            new()
            {
                TypedData = TypedDataFactory.GetTypedData(sensorValue),
                SensorType = SensorTypeFactory.GetSensorType(sensorValue),
                Time = sensorValue.Time.ToUniversalTime(),
            };
    }
}
