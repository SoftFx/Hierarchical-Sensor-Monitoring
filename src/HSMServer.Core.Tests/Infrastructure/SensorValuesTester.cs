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
            switch (expected)
            {
                case BoolSensorValue boolSensorValue:
                    TestSensorDataFromCache(boolSensorValue, actual);
                    break;
                case IntSensorValue intSensorValue:
                    TestSensorDataFromCache(intSensorValue, actual);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    TestSensorDataFromCache(doubleSensorValue, actual);
                    break;
                case StringSensorValue stringSensorValue:
                    TestSensorDataFromCache(stringSensorValue, actual);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    TestSensorDataFromCache(intBarSensorValue, actual);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    TestSensorDataFromCache(doubleBarSensorValue, actual);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    TestSensorDataFromCache(fileSensorBytesValue, actual);
                    break;
                case FileSensorValue fileSensorValue:
                    TestSensorDataFromCache(fileSensorValue, actual);
                    break;
            };
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
            switch (expected)
            {
                case BoolSensorValue boolSensorValue:
                    TestSensorInfoFromDB(boolSensorValue, actual);
                    break;
                case IntSensorValue intSensorValue:
                    TestSensorInfoFromDB(intSensorValue, actual);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    TestSensorInfoFromDB(doubleSensorValue, actual);
                    break;
                case StringSensorValue stringSensorValue:
                    TestSensorInfoFromDB(stringSensorValue, actual);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    TestSensorInfoFromDB(intBarSensorValue, actual);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    TestSensorInfoFromDB(doubleBarSensorValue, actual);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    TestSensorInfoFromDB(fileSensorBytesValue, actual);
                    break;
                case FileSensorValue fileSensorValue:
                    TestSensorInfoFromDB(fileSensorValue, actual);
                    break;
            };
        }

        internal static void TestSensorHistoryDataFromExtendedBarSensorData(ExtendedBarSensorData expected, SensorHistoryData actual)
        {
            Assert.Equal(expected.ValueType, actual.SensorType);

            TestSensorHistoryDataFromDB(expected.Value, actual);
        }

        internal static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorHistoryData actual)
        {
            switch (expected)
            {
                case BoolSensorValue boolSensorValue:
                    TestSensorHistoryDataFromDB(boolSensorValue, actual);
                    break;
                case IntSensorValue intSensorValue:
                    TestSensorHistoryDataFromDB(intSensorValue, actual);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    TestSensorHistoryDataFromDB(doubleSensorValue, actual);
                    break;
                case StringSensorValue stringSensorValue:
                    TestSensorHistoryDataFromDB(stringSensorValue, actual);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    TestSensorHistoryDataFromDB(intBarSensorValue, actual);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    TestSensorHistoryDataFromDB(doubleBarSensorValue, actual);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    TestSensorHistoryDataFromDB(fileSensorBytesValue, actual);
                    break;
                case FileSensorValue fileSensorValue:
                    TestSensorHistoryDataFromDB(fileSensorValue, actual);
                    break;
            };
        }

        internal static void TestSensorDataEntity(BoolSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.BooleanSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(IntSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.IntSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(DoubleSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.DoubleSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(StringSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.StringSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(IntBarSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.IntegerBarSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(DoubleBarSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.DoubleBarSensor, actual.DataType);
        }

        internal static void TestSensorDataEntity(FileSensorBytesValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.FileSensorBytes, actual.DataType);
        }

        internal static void TestSensorDataEntity(FileSensorValue expected, SensorDataEntity actual, DateTime timeCollected)
        {
            TestSensorDataEntity(expected, actual, GetSensorValueTypedData(expected), timeCollected);

            Assert.Equal((byte)SensorType.FileSensor, actual.DataType);
        }


        private static void TestSensorDataEntity(SensorValueBase expected, SensorDataEntity actual,
            object expectedTypedData, DateTime timeCollected)
        {
            var timeSpan = expected.Time - DateTime.UnixEpoch;

            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal((byte)expected.Status, actual.Status);
            Assert.Equal(JsonSerializer.Serialize(expectedTypedData), actual.TypedData);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(timeCollected, actual.TimeCollected);
            Assert.Equal((long)timeSpan.TotalSeconds, actual.Timestamp);
        }


        private void TestSensorDataFromCache(BoolSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.BooleanSensor, actual);

            Assert.Equal(expected.BoolValue.ToString(), actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, expected.BoolValue), actual.StringValue);
        }

        private void TestSensorDataFromCache(IntSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.IntSensor, actual);

            Assert.Equal(expected.IntValue.ToString(), actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, expected.IntValue), actual.StringValue);
        }

        private void TestSensorDataFromCache(DoubleSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.DoubleSensor, actual);

            Assert.Equal(expected.DoubleValue.ToString(), actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, expected.DoubleValue), actual.StringValue);
        }

        private void TestSensorDataFromCache(StringSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.StringSensor, actual);

            Assert.Equal(expected.StringValue, actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.Time, expected.Comment, expected.StringValue), actual.StringValue);
        }

        private void TestSensorDataFromCache(IntBarSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.IntegerBarSensor, actual);

            Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(expected.Min, expected.Mean, expected.Max, expected.Count, expected.LastValue),
                         actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.Time, expected.Comment, expected.Min, expected.Mean, expected.Max, expected.Count, expected.LastValue),
                         actual.StringValue);
        }

        private void TestSensorDataFromCache(DoubleBarSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.DoubleBarSensor, actual);

            Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(expected.Min, expected.Mean, expected.Max, expected.Count, expected.LastValue),
                         actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.Time, expected.Comment, expected.Min, expected.Mean, expected.Max, expected.Count, expected.LastValue),
                         actual.StringValue);
        }

        private void TestSensorDataFromCache(FileSensorBytesValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.FileSensorBytes, actual);

            Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(expected.FileName, expected.Extension, expected.FileContent.Length),
                         actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.Time, expected.Comment, expected.FileName, expected.Extension, expected.FileContent.Length),
                         actual.StringValue);
        }

        private void TestSensorDataFromCache(FileSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.FileSensor, actual);

            Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(expected.FileName, expected.Extension, expected.FileContent.Length),
                         actual.ShortStringValue);
            Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.Time, expected.Comment, expected.FileName, expected.Extension, expected.FileContent.Length),
                         actual.StringValue);
        }

        private void TestSensorDataFromCache(SensorValueBase expected, SensorType expectedType, SensorData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_productName, actual.Product);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(SensorStatus.Ok, actual.Status);
            //Assert.Equal(TransactionType.Add, actual.TransactionType);
            Assert.True(string.IsNullOrEmpty(actual.ValidationError));
            Assert.NotEqual(default, actual.Time);
        }


        private void TestSensorInfoFromDB(BoolSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.BooleanSensor, actual);

        private void TestSensorInfoFromDB(IntSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.IntSensor, actual);

        private void TestSensorInfoFromDB(DoubleSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.DoubleSensor, actual);

        private void TestSensorInfoFromDB(StringSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.StringSensor, actual);

        private void TestSensorInfoFromDB(IntBarSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.IntegerBarSensor, actual);

        private void TestSensorInfoFromDB(DoubleBarSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.DoubleBarSensor, actual);

        private void TestSensorInfoFromDB(FileSensorBytesValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.FileSensorBytes, actual);

        private void TestSensorInfoFromDB(FileSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.FileSensor, actual);

        private void TestSensorInfoFromDB(SensorValueBase expected, SensorType expectedType, SensorInfo actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_productName, actual.ProductName);
            Assert.Equal(expected.Path, actual.SensorName);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(default, actual.ExpectedUpdateInterval);
            Assert.Empty(actual.ValidationParameters);
            Assert.Null(actual.Unit);
        }


        private static void TestSensorHistoryDataFromDB(BoolSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.BooleanSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(IntSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.IntSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(DoubleSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.DoubleSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(StringSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.StringSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(IntBarSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.IntegerBarSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(DoubleBarSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.DoubleBarSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(FileSensorBytesValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.FileSensorBytes, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(FileSensorValue expected, SensorHistoryData actual) =>
            TestSensorHistoryDataFromDB(expected, SensorType.FileSensor, GetSensorValueTypedData(expected), actual);

        private static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorType expectedType, object expectedTypeData, SensorHistoryData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(expected.Time.ToUniversalTime(), actual.Time);
            Assert.Contains(expected.Comment, actual.TypedData);
            Assert.Equal(JsonSerializer.Serialize(expectedTypeData), actual.TypedData);
        }


        private static object GetSensorValueTypedData(ExtendedBarSensorData sensorData) =>
            sensorData.Value switch
            {
                IntBarSensorValue intBarSensorValue => GetSensorValueTypedData(intBarSensorValue),
                DoubleBarSensorValue doubleBarSensorValue => GetSensorValueTypedData(doubleBarSensorValue),
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
    }
}
