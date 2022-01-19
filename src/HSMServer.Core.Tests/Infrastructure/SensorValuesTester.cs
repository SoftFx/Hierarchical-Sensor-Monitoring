using System;
using System.Collections.Generic;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using Xunit;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class SensorValuesTester
    {
        private readonly string _productName;


        internal SensorValuesTester(DatabaseAdapterManager dbManager) =>
            _productName = dbManager.TestProduct.Name;

        internal SensorValuesTester(string productName) =>
            _productName = productName;


        internal void TestSensorDataFromCache(SensorValueBase expected, SensorData actual, DateTime? timeCollected = null)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_productName, actual.Product);
            Assert.Equal(GetSensorValueType(expected), actual.SensorType);
            Assert.Equal(SensorStatus.Ok, actual.Status);
            //Assert.Equal(TransactionType.Add, actual.TransactionType);
            Assert.True(string.IsNullOrEmpty(actual.ValidationError));
            Assert.NotEqual(default, actual.Time);

            TestSensorDataStringValues(expected, actual, timeCollected);
        }

        internal void TestSensorData(SensorValueBase expected, SensorData actual,
            DateTime timeCollected, TransactionType type)
        {
            TestSensorDataFromCache(expected, actual, timeCollected);

            Assert.Equal(timeCollected, actual.Time);
            Assert.Equal(type, actual.TransactionType);
        }

        internal void TestSensorInfoFromDB(SensorValueBase expected, SensorInfo actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_productName, actual.ProductName);
            Assert.Equal(expected.Path, actual.SensorName);
            Assert.Equal(GetSensorValueType(expected), actual.SensorType);
            Assert.Equal(default, actual.ExpectedUpdateInterval);
            Assert.Empty(actual.ValidationParameters);
            Assert.Null(actual.Unit);
        }

        internal static void TestSensorHistoryDataFromExtendedBarSensorData(ExtendedBarSensorData expected, SensorHistoryData actual)
        {
            Assert.Equal(expected.ValueType, actual.SensorType);

            TestSensorHistoryDataFromDB(expected.Value, actual);
        }

        internal static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorHistoryData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(GetSensorValueType(expected), actual.SensorType);
            Assert.Equal(expected.Time.ToUniversalTime(), actual.Time);
            Assert.Contains(expected.Comment, actual.TypedData);
            Assert.Equal(JsonSerializer.Serialize(GetSensorValueTypedData(expected)), actual.TypedData);
        }

        internal static void TestSensorDataEntity(SensorValueBase expected, SensorDataEntity actual, DateTime timeCollected)
        {
            var timeSpan = expected.Time - DateTime.UnixEpoch;

            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal((byte)expected.Status, actual.Status);
            Assert.Equal(JsonSerializer.Serialize(GetSensorValueTypedData(expected)), actual.TypedData);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(timeCollected, actual.TimeCollected);
            Assert.Equal((long)timeSpan.TotalSeconds, actual.Timestamp);
            Assert.Equal((byte)GetSensorValueType(expected), actual.DataType);
        }

        internal static void TestBarSensorFromUnitedSensor(UnitedSensorValue expected, BarSensorValueBase actual)
        {
            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Time, actual.Time);

            switch (expected.Type)
            {
                case SensorType.IntegerBarSensor:
                    TestUnitedSensorValueData(expected, (IntBarSensorValue)actual);
                    break;
                case SensorType.DoubleBarSensor:
                    TestUnitedSensorValueData(expected, (DoubleBarSensorValue)actual);
                    break;
            }
        }


        private static void TestUnitedSensorValueData(UnitedSensorValue expected, DoubleBarSensorValue actual)
        {
            var barData = JsonSerializer.Deserialize<DoubleBarData>(expected.Data);

            Assert.Equal(barData.Min, actual.Min);
            Assert.Equal(barData.Max, actual.Max);
            Assert.Equal(barData.Mean, actual.Mean);
            Assert.Equal(barData.LastValue, actual.LastValue);
            Assert.Equal(barData.Count, actual.Count);
            Assert.Equal(barData.StartTime, actual.StartTime);
            Assert.Equal(barData.EndTime, actual.EndTime);

            Assert.Equal(barData.Percentiles.Count, actual.Percentiles.Count);
            for (int i = 0; i < barData.Percentiles.Count; ++i)
            {
                Assert.Equal(barData.Percentiles[i].Value, actual.Percentiles[i].Value);
                Assert.Equal(barData.Percentiles[i].Percentile, actual.Percentiles[i].Percentile);
            }
        }

        private static void TestUnitedSensorValueData(UnitedSensorValue expected, IntBarSensorValue actual)
        {
            var barData = JsonSerializer.Deserialize<IntBarData>(expected.Data);

            Assert.Equal(barData.Min, actual.Min);
            Assert.Equal(barData.Max, actual.Max);
            Assert.Equal(barData.Mean, actual.Mean);
            Assert.Equal(barData.LastValue, actual.LastValue);
            Assert.Equal(barData.Count, actual.Count);
            Assert.Equal(barData.StartTime, actual.StartTime);
            Assert.Equal(barData.EndTime, actual.EndTime);

            Assert.Equal(barData.Percentiles.Count, actual.Percentiles.Count);
            for (int i = 0; i < barData.Percentiles.Count; ++i)
            {
                Assert.Equal(barData.Percentiles[i].Value, actual.Percentiles[i].Value);
                Assert.Equal(barData.Percentiles[i].Percentile, actual.Percentiles[i].Percentile);
            }
        }


        internal static SensorType GetSensorValueType(SensorValueBase sensorValue) =>
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
               UnitedSensorValue unitedSensorValue => unitedSensorValue.Type,
               _ => (SensorType)0,
           };

        private static void TestSensorDataStringValues(SensorValueBase expected, SensorData actual, DateTime? timeCollected = null)
        {
            switch (expected)
            {
                case BoolSensorValue boolSensorValue:
                    Assert.Equal(boolSensorValue.BoolValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(timeCollected ?? actual.Time, expected.Comment, boolSensorValue.BoolValue), actual.StringValue);
                    break;
                case IntSensorValue intSensorValue:
                    Assert.Equal(intSensorValue.IntValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(timeCollected ?? actual.Time, expected.Comment, intSensorValue.IntValue), actual.StringValue);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    Assert.Equal(doubleSensorValue.DoubleValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(timeCollected ?? actual.Time, expected.Comment, doubleSensorValue.DoubleValue), actual.StringValue);
                    break;
                case StringSensorValue stringSensorValue:
                    Assert.Equal(stringSensorValue.StringValue, actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(timeCollected ?? actual.Time, expected.Comment, stringSensorValue.StringValue), actual.StringValue);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(intBarSensorValue.Min, intBarSensorValue.Mean, intBarSensorValue.Max, intBarSensorValue.Count, intBarSensorValue.LastValue),
                        actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(timeCollected ?? actual.Time, expected.Comment, intBarSensorValue.Min, intBarSensorValue.Mean, intBarSensorValue.Max, intBarSensorValue.Count, intBarSensorValue.LastValue),
                                 actual.StringValue);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(doubleBarSensorValue.Min, doubleBarSensorValue.Mean, doubleBarSensorValue.Max, doubleBarSensorValue.Count, doubleBarSensorValue.LastValue),
                          actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(timeCollected ?? actual.Time, expected.Comment, doubleBarSensorValue.Min, doubleBarSensorValue.Mean, doubleBarSensorValue.Max, doubleBarSensorValue.Count, doubleBarSensorValue.LastValue),
                                 actual.StringValue);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorBytesValue.FileName, fileSensorBytesValue.Extension, fileSensorBytesValue.FileContent.Length),
                          actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(timeCollected ?? actual.Time, expected.Comment, fileSensorBytesValue.FileName, fileSensorBytesValue.Extension, fileSensorBytesValue.FileContent.Length),
                                 actual.StringValue);
                    break;
                case FileSensorValue fileSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorValue.FileName, fileSensorValue.Extension, fileSensorValue.FileContent.Length),
                         actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(timeCollected ?? actual.Time, expected.Comment, fileSensorValue.FileName, fileSensorValue.Extension, fileSensorValue.FileContent.Length),
                                 actual.StringValue);
                    break;
                case UnitedSensorValue unitedSensorValue:
                    Assert.Equal(GetUnitedSensorValueShortStringValue(unitedSensorValue), actual.ShortStringValue);
                    Assert.Equal(GetUnitedSensorValueStringValue(unitedSensorValue, timeCollected ?? actual.Time), actual.StringValue);
                    break;
            }
        }

        private static string GetUnitedSensorValueShortStringValue(UnitedSensorValue value)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                case SensorType.IntSensor:
                case SensorType.DoubleSensor:
                case SensorType.StringSensor:
                    return value.Data;
                case SensorType.IntegerBarSensor:
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(value.Data);
                    return SensorDataStringValuesFactory.GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                    return SensorDataStringValuesFactory.GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                default:
                    return string.Empty;
            }
        }

        private static string GetUnitedSensorValueStringValue(UnitedSensorValue value, DateTime timeCollected)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                case SensorType.IntSensor:
                case SensorType.DoubleSensor:
                case SensorType.StringSensor:
                    return SensorDataStringValuesFactory.GetSimpleSensorsString(timeCollected, value.Comment, value.Data);
                case SensorType.IntegerBarSensor:
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(value.Data);
                    return SensorDataStringValuesFactory.GetBarSensorsString(timeCollected, value.Comment, intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                    return SensorDataStringValuesFactory.GetBarSensorsString(timeCollected, value.Comment, doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                default:
                    return string.Empty;
            }
        }

        private static object GetSensorValueTypedData(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue boolSensorValue => GetSensorValueTypedData(boolSensorValue),
                IntSensorValue intSensorValue => GetSensorValueTypedData(intSensorValue),
                DoubleSensorValue doubleSensorValue => GetSensorValueTypedData(doubleSensorValue),
                StringSensorValue stringSensorValue => GetSensorValueTypedData(stringSensorValue),
                IntBarSensorValue intBarSensorValue => GetSensorValueTypedData(intBarSensorValue),
                DoubleBarSensorValue doubleBarSensorValue => GetSensorValueTypedData(doubleBarSensorValue),
                FileSensorBytesValue fileSensorBytesValue => GetSensorValueTypedData(fileSensorBytesValue),
                FileSensorValue fileSensorValue => GetSensorValueTypedData(fileSensorValue),
                UnitedSensorValue unitedSensorValue => GetSensorValueTypedData(unitedSensorValue),
                _ => null,
            };

        private static BoolSensorData GetSensorValueTypedData(BoolSensorValue sensorValue) =>
            GetBoolSensorData(sensorValue.BoolValue, sensorValue.Comment);

        private static IntSensorData GetSensorValueTypedData(IntSensorValue sensorValue) =>
            GetIntSensorData(sensorValue.IntValue, sensorValue.Comment);

        private static DoubleSensorData GetSensorValueTypedData(DoubleSensorValue sensorValue) =>
            GetDoubleSensorData(sensorValue.DoubleValue, sensorValue.Comment);

        private static StringSensorData GetSensorValueTypedData(StringSensorValue sensorValue) =>
            GetStringSensorData(sensorValue.StringValue, sensorValue.Comment);

        private static IntBarSensorData GetSensorValueTypedData(IntBarSensorValue sensorValue) =>
            GetIntBarSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue, sensorValue.Count,
                sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static DoubleBarSensorData GetSensorValueTypedData(DoubleBarSensorValue sensorValue) =>
            GetDoubleBarSensorData(sensorValue.Min, sensorValue.Max, sensorValue.Mean, sensorValue.LastValue, sensorValue.Count,
                sensorValue.StartTime, sensorValue.EndTime, sensorValue.Percentiles, sensorValue.Comment);

        private static FileSensorBytesData GetSensorValueTypedData(FileSensorBytesValue sensorValue) =>
            GetFileSensorBytesData(sensorValue.Extension, sensorValue.FileName, sensorValue.FileContent, sensorValue.Comment);

        private static FileSensorData GetSensorValueTypedData(FileSensorValue sensorValue) =>
            GetFileSensorData(sensorValue.Extension, sensorValue.FileName, sensorValue.FileContent, sensorValue.Comment);

        private static object GetSensorValueTypedData(UnitedSensorValue sensorValue) =>
            sensorValue.Type switch
            {
                SensorType.BooleanSensor => GetBoolSensorData(bool.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.IntSensor => GetIntSensorData(int.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.DoubleSensor => GetDoubleSensorData(double.Parse(sensorValue.Data), sensorValue.Comment),
                SensorType.StringSensor => GetStringSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.IntegerBarSensor => GetIntBarSensorData(sensorValue.Data, sensorValue.Comment),
                SensorType.DoubleBarSensor => GetDoubleBarSensorData(sensorValue.Data, sensorValue.Comment),
                _ => null,
            };

        private static BoolSensorData GetBoolSensorData(bool value, string comment) =>
            new()
            {
                BoolValue = value,
                Comment = comment,
            };

        private static IntSensorData GetIntSensorData(int value, string comment) =>
            new()
            {
                IntValue = value,
                Comment = comment,
            };

        private static DoubleSensorData GetDoubleSensorData(double value, string comment) =>
            new()
            {
                DoubleValue = value,
                Comment = comment,
            };

        private static StringSensorData GetStringSensorData(string value, string comment) =>
            new()
            {
                StringValue = value,
                Comment = comment,
            };

        private static IntBarSensorData GetIntBarSensorData(int min, int max, int mean, int lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueInt> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime,
                EndTime = endTime,
                LastValue = lastValue,
            };

        private static IntBarSensorData GetIntBarSensorData(string data, string comment)
        {
            var intBarData = JsonSerializer.Deserialize<IntBarData>(data);

            return GetIntBarSensorData(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.LastValue,
                intBarData.Count, intBarData.StartTime, intBarData.EndTime, intBarData.Percentiles, comment);
        }

        private static DoubleBarSensorData GetDoubleBarSensorData(double min, double max, double mean, double lastValue, int count,
            DateTime startTime, DateTime endTime, List<PercentileValueDouble> percentiles, string comment) =>
            new()
            {
                Comment = comment,
                Min = min,
                Max = max,
                Mean = mean,
                Percentiles = percentiles,
                Count = count,
                StartTime = startTime,
                EndTime = endTime,
                LastValue = lastValue,
            };

        private static DoubleBarSensorData GetDoubleBarSensorData(string data, string comment)
        {
            var doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(data);

            return GetDoubleBarSensorData(doubleBarData.Min, doubleBarData.Max, doubleBarData.Mean, doubleBarData.LastValue,
                doubleBarData.Count, doubleBarData.StartTime, doubleBarData.EndTime, doubleBarData.Percentiles, comment);
        }

        private static FileSensorBytesData GetFileSensorBytesData(string extension, string filename, byte[] content, string comment) =>
            new()
            {
                Extension = extension,
                FileName = filename,
                FileContent = content,
                Comment = comment,
            };

        private static FileSensorData GetFileSensorData(string extension, string filename, string content, string comment) =>
            new()
            {
                Extension = extension,
                FileName = filename,
                FileContent = content,
                Comment = comment,
            };
    }
}
