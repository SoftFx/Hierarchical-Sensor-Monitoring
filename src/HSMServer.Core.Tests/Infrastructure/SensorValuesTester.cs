using System;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
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


        internal void TestSensorDataFromCache(SensorValueBase expected, SensorData actual)
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

            TestSensorDataStringValues(expected, actual);
        }

        internal void TestSensorData(SensorValueBase expected, SensorData actual,
            DateTime timeCollected, TransactionType type)
        {
            TestSensorDataFromCache(expected, actual);

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


        private static void TestSensorDataStringValues(SensorValueBase expected, SensorData actual)
        {
            switch (expected)
            {
                case BoolSensorValue boolSensorValue:
                    Assert.Equal(boolSensorValue.BoolValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, boolSensorValue.BoolValue), actual.StringValue);
                    break;
                case IntSensorValue intSensorValue:
                    Assert.Equal(intSensorValue.IntValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, intSensorValue.IntValue), actual.StringValue);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    Assert.Equal(doubleSensorValue.DoubleValue.ToString(), actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, doubleSensorValue.DoubleValue), actual.StringValue);
                    break;
                case StringSensorValue stringSensorValue:
                    Assert.Equal(stringSensorValue.StringValue, actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, stringSensorValue.StringValue), actual.StringValue);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(intBarSensorValue.Min, intBarSensorValue.Mean, intBarSensorValue.Max, intBarSensorValue.Count, intBarSensorValue.LastValue),
                        actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.Time, expected.Comment, intBarSensorValue.Min, intBarSensorValue.Mean, intBarSensorValue.Max, intBarSensorValue.Count, intBarSensorValue.LastValue),
                                 actual.StringValue);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(doubleBarSensorValue.Min, doubleBarSensorValue.Mean, doubleBarSensorValue.Max, doubleBarSensorValue.Count, doubleBarSensorValue.LastValue),
                          actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.Time, expected.Comment, doubleBarSensorValue.Min, doubleBarSensorValue.Mean, doubleBarSensorValue.Max, doubleBarSensorValue.Count, doubleBarSensorValue.LastValue),
                                 actual.StringValue);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorBytesValue.FileName, fileSensorBytesValue.Extension, fileSensorBytesValue.FileContent.Length),
                          actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.Time, expected.Comment, fileSensorBytesValue.FileName, fileSensorBytesValue.Extension, fileSensorBytesValue.FileContent.Length),
                                 actual.StringValue);
                    break;
                case FileSensorValue fileSensorValue:
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorValue.FileName, fileSensorValue.Extension, fileSensorValue.FileContent.Length),
                         actual.ShortStringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.Time, expected.Comment, fileSensorValue.FileName, fileSensorValue.Extension, fileSensorValue.FileContent.Length),
                                 actual.StringValue);
                    break;
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
                _ => null,
            };

        private static object GetSensorValueTypedData(BoolSensorValue sensorValue) =>
            new
            {
                sensorValue.Comment,
                sensorValue.BoolValue,
            };

        private static object GetSensorValueTypedData(IntSensorValue sensorValue) =>
            new
            {
                sensorValue.IntValue,
                sensorValue.Comment,
            };

        private static object GetSensorValueTypedData(DoubleSensorValue sensorValue) =>
            new
            {
                sensorValue.DoubleValue,
                sensorValue.Comment,
            };

        private static object GetSensorValueTypedData(StringSensorValue sensorValue) =>
            new
            {
                sensorValue.StringValue,
                sensorValue.Comment,
            };

        private static object GetSensorValueTypedData(IntBarSensorValue sensorValue) =>
            new
            {
                sensorValue.Comment,
                sensorValue.Min,
                sensorValue.Max,
                sensorValue.Mean,
                sensorValue.Percentiles,
                sensorValue.Count,
                sensorValue.StartTime,
                sensorValue.EndTime,
                sensorValue.LastValue,
            };

        private static object GetSensorValueTypedData(DoubleBarSensorValue sensorValue) =>
            new
            {
                sensorValue.Comment,
                sensorValue.Min,
                sensorValue.Max,
                sensorValue.Mean,
                sensorValue.Count,
                sensorValue.Percentiles,
                sensorValue.StartTime,
                sensorValue.EndTime,
                sensorValue.LastValue,
            };

        private static object GetSensorValueTypedData(FileSensorBytesValue sensorValue) =>
            new
            {
                sensorValue.Comment,
                sensorValue.Extension,
                sensorValue.FileContent,
                sensorValue.FileName,
            };

        private static object GetSensorValueTypedData(FileSensorValue sensorValue) =>
            new
            {
                sensorValue.Comment,
                sensorValue.Extension,
                sensorValue.FileContent,
                sensorValue.FileName,
            };

        private static SensorType GetSensorValueType(SensorValueBase sensorValue) =>
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
    }
}
