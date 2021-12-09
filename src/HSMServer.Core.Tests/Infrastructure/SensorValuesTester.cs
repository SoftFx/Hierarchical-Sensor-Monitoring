using System.Text.Json;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    internal sealed class SensorValuesTester
    {
        private readonly string _productName;


        internal SensorValuesTester(DatabaseAdapterManager dbManager) =>
            _productName = dbManager.TestProduct.Name;


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


        private void TestSensorDataFromCache(BoolSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.BooleanSensor, actual);

            Assert.Equal(expected.BoolValue.ToString(), actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.BoolValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(IntSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.IntSensor, actual);

            Assert.Equal(expected.IntValue.ToString(), actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.IntValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(DoubleSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.DoubleSensor, actual);

            Assert.Equal(expected.DoubleValue.ToString(), actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.DoubleValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(StringSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.StringSensor, actual);

            Assert.Equal(expected.StringValue, actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.StringValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(IntBarSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.IntegerBarSensor, actual);

            Assert.Equal($"Min = {expected.Min}, Mean = {expected.Mean}, Max = {expected.Max}, Count = {expected.Count}, Last = {expected.LastValue}.", actual.ShortStringValue);
            Assert.EndsWith($". Value: Min = {expected.Min}, Mean = {expected.Mean}, Max = {expected.Max}, Count = {expected.Count}, Last = {expected.LastValue}. Comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(DoubleBarSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.DoubleBarSensor, actual);

            Assert.Equal($"Min = {expected.Min}, Mean = {expected.Mean}, Max = {expected.Max}, Count = {expected.Count}, Last = {expected.LastValue}.", actual.ShortStringValue);
            Assert.EndsWith($". Value: Min = {expected.Min}, Mean = {expected.Mean}, Max = {expected.Max}, Count = {expected.Count}, Last = {expected.LastValue}. Comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(FileSensorBytesValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.FileSensorBytes, actual);

            Assert.Equal($"File size: {expected.FileContent.Length} bytes. File name: {expected.FileName}.{expected.Extension}.", actual.ShortStringValue);
            Assert.EndsWith($". File size: {expected.FileContent.Length} bytes. File name: {expected.FileName}.{expected.Extension}. Comment = {expected.Comment}.", actual.StringValue);
        }

        private void TestSensorDataFromCache(FileSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.FileSensor, actual);

            Assert.Equal($"File size: {expected.FileContent.Length} bytes. File name: {expected.FileName}.{expected.Extension}.", actual.ShortStringValue);
            Assert.EndsWith($". File size: {expected.FileContent.Length} bytes. File name: {expected.FileName}.{expected.Extension}. Comment = {expected.Comment}.", actual.StringValue);
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

        private static void TestSensorHistoryDataFromDB(BoolSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.Comment,
                expected.BoolValue,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.BooleanSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(IntSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.IntValue,
                expected.Comment,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.IntSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(DoubleSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.DoubleValue,
                expected.Comment,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.DoubleSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(StringSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.StringValue,
                expected.Comment,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.StringSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(IntBarSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.Comment,
                expected.Min,
                expected.Max,
                expected.Mean,
                expected.Percentiles,
                expected.Count,
                expected.StartTime,
                expected.EndTime,
                expected.LastValue,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.IntegerBarSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(DoubleBarSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.Comment,
                expected.Min,
                expected.Max,
                expected.Mean,
                expected.Count,
                expected.Percentiles,
                expected.StartTime,
                expected.EndTime,
                expected.LastValue,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.DoubleBarSensor, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(FileSensorBytesValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.Comment,
                expected.Extension,
                expected.FileContent,
                expected.FileName,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.FileSensorBytes, typedData, actual);
        }

        private static void TestSensorHistoryDataFromDB(FileSensorValue expected, SensorHistoryData actual)
        {
            var typedData = new
            {
                expected.Comment,
                expected.Extension,
                expected.FileContent,
                expected.FileName,
            };

            TestSensorHistoryDataFromDB(expected, SensorType.FileSensor, typedData, actual);
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
            Assert.Equal(string.Empty, actual.ValidationError);
            Assert.NotEqual(default, actual.Time);
        }

        private static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorType expectedType, object expectedTypeData, SensorHistoryData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(expected.Time.ToUniversalTime(), actual.Time);
            Assert.Contains(expected.Comment, actual.TypedData);
            Assert.Equal(JsonSerializer.Serialize(expectedTypeData), actual.TypedData);
        }

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
    }
}
