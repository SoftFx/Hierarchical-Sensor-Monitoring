using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal class ModelsTester
    {
        internal static void TestProductModel(ProductEntity expected, ProductModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ParentProductId, actual.ParentProduct?.Id);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.State, (int)actual.State);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.CreationDate, actual.CreationDate.Ticks);

            var expectedSubProducts = expected.SubProductsIds;
            var actualSubProducts = actual.SubProducts.Select(p => p.Key).ToList();
            TestCollections(expectedSubProducts, actualSubProducts);

            var expectedSensors = expected.SensorsIds;
            var actualSensors = actual.Sensors.Select(p => p.Key.ToString()).ToList();
            TestCollections(expectedSensors, actualSensors);
        }


        internal static void TestSensorModel(SensorEntity expected, SensorModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id.ToString());
            Assert.Equal(expected.ProductId, actual.ParentProduct?.Id);
            Assert.Equal(expected.SensorName, actual.SensorName);
            Assert.Equal(expected.ProductName, actual.ProductName);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.ExpectedUpdateIntervalTicks, actual.ExpectedUpdateInterval.Ticks);
            Assert.Equal(expected.Unit, actual.Unit);
            Assert.Equal(expected.SensorType, (int)actual.SensorType);
            Assert.True(string.IsNullOrEmpty(actual.ValidationError));
        }

        internal static void TestSensorModel(SensorDataEntity expectedSensorData, SensorModel actual)
        {
            Assert.NotNull(actual);
            Assert.NotNull(expectedSensorData);

            Assert.Equal(expectedSensorData.Path, actual.Path);
            Assert.Equal(expectedSensorData.DataType, (byte)actual.SensorType);
            Assert.Equal(expectedSensorData.Time, actual.SensorTime);
            Assert.Equal(expectedSensorData.TimeCollected, actual.LastUpdateTime);
            Assert.Equal(expectedSensorData.Status, (byte)actual.Status);
            Assert.Equal(expectedSensorData.TypedData, actual.TypedData);
            Assert.Equal(expectedSensorData.OriginalFileSensorContentSize, actual.OriginalFileSensorContentSize);

            if (expectedSensorData.Timestamp != 0)
                Assert.Equal(expectedSensorData.Timestamp, actual.SensorTime.GetTimestamp());
        }

        internal static void TestSensorModel(SensorValueBase expected, string expectedProduct, DateTime timeCollected, SensorModel actual)
        {
            Assert.NotNull(actual);
            Assert.False(string.IsNullOrEmpty(actual.Id.ToString()));
            Assert.True(string.IsNullOrEmpty(actual.ParentProduct?.Id));
            Assert.Equal(expected.Path.GetSensorName(), actual.SensorName);
            Assert.Equal(expectedProduct, actual.ProductName);
            Assert.Equal(expected.Path, actual.Path);

            TestSensorModelData(expected, timeCollected, actual);
        }

        internal static void TestSensorModel(SensorUpdate expected, SensorModel actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(TimeSpan.Parse(expected.ExpectedUpdateInterval), actual.ExpectedUpdateInterval);
            Assert.Equal(expected.Unit, actual.Unit);
        }

        internal static void TestSensorModelData(SensorValueBase expected, DateTime timeCollected, SensorModel actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(SensorValuesTester.GetSensorValueType(expected), actual.SensorType);
            Assert.Equal(SensorValuesTester.GetSensorValueTypedDataString(expected), actual.TypedData);
            Assert.Equal(expected.Time, actual.SensorTime);
            Assert.Equal(timeCollected, actual.LastUpdateTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.True(string.IsNullOrEmpty(actual.ValidationError));
        }


        private static void TestCollections(List<string> expected, List<string> actual)
        {
            expected.Sort();
            actual.Sort();

            Assert.Equal(expected, actual);
        }
    }
}
