using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesExtensions
    {
        public static BarSensorValueBase Convert(this UnitedSensorValue value) =>
            BuildBarSensorValue(value)?.FillBarSensorValueCommonSettings(value);


        private static BarSensorValueBase BuildBarSensorValue(UnitedSensorValue unitedSensorValue) =>
            unitedSensorValue.Type switch
            {
                SensorType.IntegerBarSensor => BuildIntBarSensorValue(unitedSensorValue),
                SensorType.DoubleBarSensor => BuildDoubleBarSensorValue(unitedSensorValue),
                _ => null,
            };

        private static IntBarSensorValue BuildIntBarSensorValue(UnitedSensorValue unitedSensorValue)
        {
            var data = JsonSerializer.Deserialize<IntBarData>(unitedSensorValue.Data);

            return new()
            {
                Max = data.Max,
                Mean = data.Mean,
                Min = data.Min,
                Percentiles = data.Percentiles,
                LastValue = data.LastValue,
                Count = data.Count,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
            };
        }

        private static DoubleBarSensorValue BuildDoubleBarSensorValue(UnitedSensorValue unitedSensorValue)
        {
            var data = JsonSerializer.Deserialize<DoubleBarData>(unitedSensorValue.Data);

            return new()
            {
                Max = data.Max,
                Mean = data.Mean,
                Min = data.Min,
                Percentiles = data.Percentiles,
                LastValue = data.LastValue,
                Count = data.Count,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
            };
        }

        private static BarSensorValueBase FillBarSensorValueCommonSettings(this BarSensorValueBase sensorValue, UnitedSensorValue unitedSensorValue)
        {
            sensorValue.Comment = unitedSensorValue.Comment;
            sensorValue.Path = unitedSensorValue.Path;
            sensorValue.Description = unitedSensorValue.Description;
            sensorValue.Status = unitedSensorValue.Status;
            sensorValue.Key = unitedSensorValue.Key;
            sensorValue.Time = unitedSensorValue.Time;

            return sensorValue;
        }
    }
}
