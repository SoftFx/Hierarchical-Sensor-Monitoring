using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class ModelsTester
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

        internal static void TestProductModel(ProductModel expected, ProductModel actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ParentProduct?.Id, actual.ParentProduct?.Id);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.CreationDate, actual.CreationDate);

            var expectedSubProducts = expected.SubProducts.Select(p => p.Key).ToList();
            var actualSubProducts = actual.SubProducts.Select(p => p.Key).ToList();
            TestCollections(expectedSubProducts, actualSubProducts);

            var expectedSensors = expected.Sensors.Select(p => p.Key.ToString()).ToList();
            var actualSensors = actual.Sensors.Select(p => p.Key.ToString()).ToList();
            TestCollections(expectedSensors, actualSensors);
        }

        internal static void TestProductModel(string name, ProductModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(name, actual.DisplayName);
            Assert.Equal(ProductState.FullAccess, actual.State);
            Assert.NotEqual(DateTime.MinValue, actual.CreationDate);
            Assert.False(string.IsNullOrEmpty(actual.Id));
            Assert.True(string.IsNullOrEmpty(actual.Description));
            Assert.Null(actual.ParentProduct);
            Assert.Empty(actual.SubProducts);
            Assert.Empty(actual.Sensors);
        }

        internal static void TestProducts(List<ProductEntity> expected, List<ProductModel> actual)
        {
            var actualDict = actual.ToDictionary(p => p.Id);

            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedProduct in expected)
                TestProductModel(expectedProduct, actualDict[expectedProduct.Id]);
        }


        internal static void TestSensorModel(SensorEntity expected, SensorModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.ExpectedUpdateIntervalTicks, actual.ExpectedUpdateInterval.Ticks);
            Assert.Equal(expected.Unit, actual.Unit);

            TestSensorModelWithoutUpdatedMetadata(expected, actual);
        }

        internal static void TestSensorModelWithoutUpdatedMetadata(SensorEntity expected, SensorModel actual)
        {
            Assert.Equal(expected.Id, actual.Id.ToString());
            Assert.Equal(expected.ProductId, actual.ParentProduct?.Id);
            Assert.Equal(expected.SensorName, actual.SensorName);
            Assert.Equal(expected.ProductName, actual.ProductName);
            Assert.Equal(expected.Path, actual.Path);
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

        internal static void TestSensorModel(SensorModel expected, SensorModel actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.ExpectedUpdateInterval, actual.ExpectedUpdateInterval);
            Assert.Equal(expected.Unit, actual.Unit);

            TestSensorModelWithoutUpdatedMetadata(expected, actual);
        }

        internal static void TestSensorModelWithoutUpdatedMetadata(SensorModel expected, SensorModel actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ParentProduct.Id, actual.ParentProduct.Id);
            Assert.Equal(expected.SensorName, actual.SensorName);
            Assert.Equal(expected.ProductName, actual.ProductName);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.SensorType, actual.SensorType);
            Assert.Equal(expected.ValidationError, actual.ValidationError);
            Assert.Equal(expected.SensorTime, actual.SensorTime);
            Assert.Equal(expected.LastUpdateTime, actual.LastUpdateTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.TypedData, actual.TypedData);
            Assert.Equal(expected.OriginalFileSensorContentSize, actual.OriginalFileSensorContentSize);
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

        internal static void TestSensorModel(SensorUpdate expected, SensorEntity actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(TimeSpan.Parse(expected.ExpectedUpdateInterval).Ticks, actual.ExpectedUpdateIntervalTicks);
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
