using System;
using System.Collections.Generic;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class SensorValuesFactory
    {
        private readonly string _productKey;


        internal SensorValuesFactory(DatabaseAdapterManager dbManager) =>
            _productKey = dbManager.TestProduct.Key;

        internal SensorValuesFactory(string productKey) =>
            _productKey = productKey;


        internal SensorValueBase BuildRandomSensorValue() =>
            BuildSensorValue((SensorType)RandomGenerator.GetRandomByte());

        // max: 6, because United sensor values don't exist for FileSensorValue and FileSensorBytesValue
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
                SensorType.FileSensorBytes => BuildFileSensorBytesValue(),
                SensorType.FileSensor => BuildFileSensorValue(),
                _ => null,
            };

        internal BoolSensorValue BuildBoolSensorValue()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                BoolValue = RandomGenerator.GetRandomBool(),
            };

            return boolSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal IntSensorValue BuildIntSensorValue()
        {
            var intSensorValue = new IntSensorValue()
            {
                IntValue = RandomGenerator.GetRandomInt(),
            };

            return intSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal DoubleSensorValue BuildDoubleSensorValue()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                DoubleValue = RandomGenerator.GetRandomDouble(),
            };

            return doubleSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal StringSensorValue BuildStringSensorValue()
        {
            var stringSensorValue = new StringSensorValue()
            {
                StringValue = RandomGenerator.GetRandomString(),
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

        internal FileSensorBytesValue BuildFileSensorBytesValue()
        {
            var fileSensorBytesValue = new FileSensorBytesValue()
            {
                Extension = RandomGenerator.GetRandomString(3),
                FileContent = RandomGenerator.GetRandomBytes(),
                FileName = nameof(FileSensorBytesValue),
            };

            return fileSensorBytesValue.FillCommonSensorValueProperties(_productKey);
        }

        internal FileSensorValue BuildFileSensorValue()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = RandomGenerator.GetRandomString(3),
                FileContent = RandomGenerator.GetRandomString(),
                FileName = nameof(FileSensorValue),
            };

            return fileSensorValue.FillCommonSensorValueProperties(_productKey);
        }

        internal ExtendedBarSensorData BuildExtendedBarSensorData(SensorType type) =>
            type switch
            {
                SensorType.IntegerBarSensor => BuildExtendedIntBarSensorData(),
                SensorType.DoubleBarSensor => BuildExtendedDoubleBarSensorData(),
                _ => null,
            };

        internal ExtendedBarSensorData BuildExtendedIntBarSensorData() =>
            new()
            {
                Value = BuildIntBarSensorValue(),
                ValueType = SensorType.IntegerBarSensor,
                ProductName = _productKey,
            };

        internal ExtendedBarSensorData BuildExtendedDoubleBarSensorData() =>
            new()
            {
                Value = BuildDoubleBarSensorValue(),
                ValueType = SensorType.DoubleBarSensor,
                ProductName = _productKey,
            };

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


        private static List<PercentileValueInt> GetPercentileValuesInt(int size = 2)
        {
            var percentiles = new List<PercentileValueInt>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueInt(RandomGenerator.GetRandomInt(), RandomGenerator.GetRandomDouble()));

            return percentiles;
        }

        private static List<PercentileValueDouble> GetPercentileValuesDouble(int size = 2)
        {
            var percentiles = new List<PercentileValueDouble>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(new PercentileValueDouble(RandomGenerator.GetRandomDouble(), RandomGenerator.GetRandomDouble()));

            return percentiles;
        }
    }
}
