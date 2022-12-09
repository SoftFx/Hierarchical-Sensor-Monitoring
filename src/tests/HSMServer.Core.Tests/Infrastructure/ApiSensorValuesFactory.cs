using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Text.Json;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class ApiSensorValuesFactory
    {
        private readonly string _productKey;


        internal ApiSensorValuesFactory(string productKey) =>
            _productKey = productKey;


        // max: 6, because United sensor values don't exist for FileSensorValue
        internal UnitedSensorValue BuildRandomUnitedSensorValue() =>
            BuildUnitedSensorValue((SensorType)RandomGenerator.GetRandomByte(max: 6));

        internal SensorValueBase BuildSensorValue(SensorType sensorType) =>
            sensorType switch
            {
                SensorType.BooleanSensor => BuildBoolSensorValue(),
                SensorType.IntSensor => BuildIntSensorValue(),
                SensorType.DoubleSensor => BuildDoubleSensorValue(),
                SensorType.StringSensor => BuildStringSensorValue(),
                SensorType.IntegerBarSensor => BuildIntBarSensorValue(),
                SensorType.DoubleBarSensor => BuildDoubleBarSensorValue(),
                SensorType.FileSensor => BuildFileSensorValue(),
                _ => null,
            };

        internal BoolSensorValue BuildBoolSensorValue()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                Value = RandomGenerator.GetRandomBool(),
            };

            return boolSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntSensorValue BuildIntSensorValue()
        {
            var intSensorValue = new IntSensorValue()
            {
                Value = RandomGenerator.GetRandomInt(),
            };

            return intSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal DoubleSensorValue BuildDoubleSensorValue()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                Value = RandomGenerator.GetRandomDouble(),
            };

            return doubleSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal StringSensorValue BuildStringSensorValue()
        {
            var stringSensorValue = new StringSensorValue()
            {
                Value = RandomGenerator.GetRandomString(),
            };

            return stringSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntBarSensorValue BuildIntBarSensorValue()
        {
            var intBarSensorValue = new IntBarSensorValue()
            {
                LastValue = RandomGenerator.GetRandomInt(),
                Min = RandomGenerator.GetRandomInt(),
                Max = RandomGenerator.GetRandomInt(),
                Mean = RandomGenerator.GetRandomInt(),
                Percentiles = GetPercentileValuesInt(),
            };

            return intBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal DoubleBarSensorValue BuildDoubleBarSensorValue()
        {
            var doubleBarSensorValue = new DoubleBarSensorValue()
            {
                LastValue = RandomGenerator.GetRandomDouble(),
                Min = RandomGenerator.GetRandomDouble(),
                Max = RandomGenerator.GetRandomDouble(),
                Mean = RandomGenerator.GetRandomDouble(),
                Percentiles = GetPercentileValuesDouble(),
            };

            return doubleBarSensorValue.FillCommonBarSensorValueProperties(_productKey);
        }

        internal FileSensorValue BuildFileSensorValue()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = RandomGenerator.GetRandomString(3),
                Value = RandomGenerator.GetRandomBytes(),
                FileName = nameof(FileSensorValue),
            };

            return fileSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal UnitedSensorValue BuildUnitedSensorValue(SensorType sensorType, bool isMinEndTime = false)
        {
            var sensorValue = new UnitedSensorValue
            {
                Type = sensorType,
                Data = BuildUnitedValueData(sensorType, isMinEndTime),
            };

            return sensorValue.FillCommonSensorValueProperties(_productKey, uniqPath: sensorType.ToString());
        }


        private static string BuildUnitedValueData(SensorType sensorType, bool isMinEndTime) =>
            sensorType switch
            {
                SensorType.BooleanSensor => RandomGenerator.GetRandomBool().ToString(),
                SensorType.IntSensor => RandomGenerator.GetRandomInt().ToString(),
                SensorType.DoubleSensor => RandomGenerator.GetRandomDouble().ToString(),
                SensorType.StringSensor => RandomGenerator.GetRandomString(),
                SensorType.IntegerBarSensor => JsonSerializer.Serialize(BuildIntBarData(isMinEndTime)),
                SensorType.DoubleBarSensor => JsonSerializer.Serialize(BuildDoubleBarData(isMinEndTime)),
                _ => null,
            };

        private static IntBarData BuildIntBarData(bool isMinEndTime) =>
            new()
            {
                LastValue = RandomGenerator.GetRandomInt(),
                Min = RandomGenerator.GetRandomInt(),
                Max = RandomGenerator.GetRandomInt(),
                Mean = RandomGenerator.GetRandomInt(),
                Count = RandomGenerator.GetRandomInt(positive: true),
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                EndTime = isMinEndTime ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(10),
                Percentiles = GetPercentileValuesInt(),
            };

        private static DoubleBarData BuildDoubleBarData(bool isMinEndTime) =>
            new()
            {
                LastValue = RandomGenerator.GetRandomDouble(),
                Min = RandomGenerator.GetRandomDouble(),
                Max = RandomGenerator.GetRandomDouble(),
                Mean = RandomGenerator.GetRandomDouble(),
                Count = RandomGenerator.GetRandomInt(positive: true),
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                EndTime = isMinEndTime ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(10),
                Percentiles = GetPercentileValuesDouble(),
            };


        private static Dictionary<double, int> GetPercentileValuesInt(int size = 3)
        {
            var percentiles = new Dictionary<double, int>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(RandomGenerator.GetRandomDouble(), RandomGenerator.GetRandomInt());

            return percentiles;
        }

        private static Dictionary<double, double> GetPercentileValuesDouble(int size = 3)
        {
            var percentiles = new Dictionary<double, double>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(RandomGenerator.GetRandomDouble(), RandomGenerator.GetRandomDouble());

            return percentiles;
        }
    }
}
