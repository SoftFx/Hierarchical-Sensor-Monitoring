using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;
using Xunit;

namespace HSMServer.Core.Tests
{
    internal static class SensorValuesTester
    {
        private static DatabaseAdapterFixture _databaseFixture;


        internal static void Initialize(DatabaseAdapterFixture dbFixture) =>
            _databaseFixture = dbFixture;


        internal static void TestSensorDataFromCache(BoolSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.BooleanSensor, actual);

            Assert.Equal(expected.BoolValue.ToString(), actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.BoolValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        internal static void TestSensorDataFromCache(IntSensorValue expected, SensorData actual)
        {
            TestSensorDataFromCache(expected, SensorType.IntSensor, actual);

            Assert.Equal(expected.IntValue.ToString(), actual.ShortStringValue);
            Assert.EndsWith($". Value = {expected.IntValue}, comment = {expected.Comment}.", actual.StringValue);
        }

        internal static void TestSensorHistoryDataFromDB(BoolSensorValue expected, SensorHistoryData actual)
        {
            TestSensorHistoryDataFromDB(expected, SensorType.BooleanSensor, actual);

            Assert.Contains(expected.BoolValue.ToString().ToLower(), actual.TypedData);
        }

        internal static void TestSensorHistoryDataFromDB(IntSensorValue expected, SensorHistoryData actual)
        {
            TestSensorHistoryDataFromDB(expected, SensorType.IntSensor, actual);

            Assert.Contains(expected.IntValue.ToString().ToLower(), actual.TypedData);
        }

        internal static void TestSensorInfoFromDB(BoolSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.BooleanSensor, actual);

        internal static void TestSensorInfoFromDB(IntSensorValue expected, SensorInfo actual) =>
            TestSensorInfoFromDB(expected, SensorType.IntSensor, actual);


        private static void TestSensorDataFromCache(SensorValueBase expected, SensorType expectedType, SensorData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_databaseFixture.TestProduct.Name, actual.Product);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(SensorStatus.Ok, actual.Status);
            Assert.Equal(TransactionType.Add, actual.TransactionType);
            Assert.Equal(string.Empty, actual.ValidationError);
            Assert.NotEqual(default, actual.Time);
        }

        private static void TestSensorHistoryDataFromDB(SensorValueBase expected, SensorType expectedType, SensorHistoryData actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(expected.Time.ToUniversalTime(), actual.Time);
            Assert.Contains(expected.Comment, actual.TypedData);
        }

        private static void TestSensorInfoFromDB(SensorValueBase expected, SensorType expectedType, SensorInfo actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(_databaseFixture.TestProduct.Name, actual.ProductName);
            Assert.Equal(expected.Path, actual.SensorName);
            Assert.Equal(expectedType, actual.SensorType);
            Assert.Equal(default, actual.ExpectedUpdateInterval);
            Assert.Empty(actual.ValidationParameters);
            Assert.Null(actual.Unit);
        }
    }
}
