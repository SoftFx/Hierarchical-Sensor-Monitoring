using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.SensorsUpdatesQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HSMServer.Extensions;
using Xunit;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class ModelsTester
    {
        internal static void TestProductModel(ProductEntity expected, ProductModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id.ToString());
            Assert.Equal(expected.ParentProductId, actual.Parent?.Id.ToString());
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.State, (int)actual.State);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.CreationDate, actual.CreationDate.Ticks);
        }

        internal static void TestProductModel(ProductModel expected, ProductModel actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Parent?.Id, actual.Parent?.Id);
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

            var expectedKeys = expected.AccessKeys.Values.ToList();
            var actualKeys = actual.AccessKeys.Values.ToList();
            for (int i = 0; i < expectedKeys.Count; i++)
            {
                TestAccessKeyModel(expectedKeys[i], actualKeys[i]);
            }
        }

        internal static void TestProductModel(string name, ProductModel actual,
            ProductModel parentProduct = null, List<ProductModel> subProducts = null, List<BaseSensorModel> sensors = null)
        {
            Assert.NotNull(actual);
            Assert.Equal(name, actual.DisplayName);
            Assert.Equal(ProductState.FullAccess, actual.State);
            Assert.NotEqual(DateTime.MinValue, actual.CreationDate);
            Assert.NotEqual(Guid.Empty, actual.Id);
            Assert.True(string.IsNullOrEmpty(actual.Description));
            Assert.Equal(parentProduct, actual.Parent);

            if (subProducts == null)
                Assert.Empty(actual.SubProducts);
            else
                TestCollections(subProducts.Select(s => s.Id).ToList(), actual.SubProducts.Keys.ToList());

            if (sensors == null)
                Assert.Empty(actual.Sensors);
            else
                TestCollections(sensors.Select(s => s.Id.ToString()).ToList(), actual.Sensors.Keys.Select(k => k.ToString()).ToList());
        }

        internal static void TestProducts(List<ProductEntity> expected, List<ProductModel> actual)
        {
            var actualDict = actual.ToDictionary(p => p.Id);

            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedProduct in expected)
                TestProductModel(expectedProduct, actualDict[expectedProduct.Id.ToGuid()]);
        }


        internal static void TestAccessKeyModel(AccessKeyEntity expected, AccessKeyModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id.ToString());
            Assert.Equal(expected.AuthorId, actual.AuthorId.ToString());
            Assert.Equal(expected.ProductId, actual.ProductId.ToString());
            Assert.Equal(expected.State, (byte)actual.State);
            Assert.Equal(expected.Permissions, (long)actual.Permissions);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.CreationTime, actual.CreationTime.Ticks);
            Assert.Equal(expected.ExpirationTime, actual.ExpirationTime.Ticks);
        }

        internal static void TestAccessKeyModel(AccessKeyModel expected, AccessKeyModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.AuthorId, actual.AuthorId);
            Assert.Equal(expected.ProductId, actual.ProductId);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.Permissions, actual.Permissions);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.CreationTime, actual.CreationTime);
            Assert.Equal(expected.ExpirationTime, actual.ExpirationTime);
        }

        internal static void TestAccessKeyModel(ProductModel expected, AccessKeyModel actual)
        {
            var fullPermissions = (KeyPermissions)(1 << Enum.GetValues<KeyPermissions>().Length) - 1;

            Assert.NotNull(actual);
            Assert.Equal(expected.AuthorId, actual.AuthorId);
            Assert.Equal(expected.Id, actual.ProductId);
            Assert.Equal(fullPermissions, actual.Permissions);
            Assert.Equal(CommonConstants.DefaultAccessKey, actual.DisplayName);
            Assert.Equal(DateTime.MaxValue, actual.ExpirationTime);
        }

        internal static void TestAccessKeyModel(Guid authorId, Guid productId, AccessKeyModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(authorId, actual.AuthorId);
            Assert.Equal(productId, actual.ProductId);
        }


        internal static void TestSensorModel(SensorEntity expected, BaseSensorModel actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);

            TestSensorModelWithoutUpdatedMetadata(expected, actual);
        }

        internal static void TestSensorModelWithoutUpdatedMetadata(SensorEntity expected, BaseSensorModel actual)
        {
            Assert.Equal(expected.Id, actual.Id.ToString());
            Assert.Equal(expected.ProductId, actual.Parent?.Id.ToString());
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Type, (int)actual.Type);
        }

        internal static void TestSensorModel(byte[] expectedSensorValueBytes, BaseSensorModel actual)
        {
            var expectedSensorValue = GetValue(expectedSensorValueBytes, actual.Type);
            var actualSensorValue = actual.LastValue;

            Assert.NotNull(actualSensorValue);
            Assert.NotNull(expectedSensorValue);

            Assert.True(actual.HasData);
            Assert.Equal(expectedSensorValue.ReceivingTime, actual.LastUpdateTime);

            Assert.Equal(expectedSensorValue.Status, actual.Status?.Status);
            if (expectedSensorValue.Status != SensorStatus.Ok)
                Assert.False(string.IsNullOrEmpty(actual.Status?.Message));
            else
                Assert.True(string.IsNullOrEmpty(actual.Status?.Message));

            TestSensorValue(expectedSensorValue, actualSensorValue, actual.Type);
        }

        internal static void AssertModels<T>(T actual, T expected)
        {
            var type = typeof(T);

            foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var actualValue = pi.GetValue(actual);
                var expectedValue = pi.GetValue(expected);

                if (pi.PropertyType.Assembly.FullName == type.Assembly.FullName)
                {
                    if (pi.PropertyType.IsClass)
                        AssertModels(actualValue, expectedValue);
                    else if (pi.PropertyType.IsEnum)
                        Assert.Equal(actualValue, expectedValue);
                }
                else
                    Assert.Equal(actualValue, expectedValue);
            }
        }

        internal static void TestSensorModelWithoutUpdatedMetadata(BaseSensorModel expected, BaseSensorModel actual)
        {
            TestImmutableSensorData(expected, actual);

            AssertModels(expected.LastValue, actual.LastValue);
            Assert.Equal(expected.LastUpdateTime, actual.LastUpdateTime);
            Assert.Equal(expected.HasData, actual.HasData);
        }

        internal static void TestSensorDataWithoutClearedData(BaseSensorModel expected, BaseSensorModel actual)
        {
            TestImmutableSensorData(expected, actual);

            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Settings.TTL.Value, actual.Settings.TTL.Value);
        }

        internal static void TestSensorModel(StoreInfo expected, BaseSensorModel actual, ProductModel parentProduct = null)
        {
            Assert.NotNull(actual);
            Assert.False(string.IsNullOrEmpty(actual.Id.ToString()));
            Assert.Equal(expected.Path.GetSensorName(), actual.DisplayName);
            Assert.Equal(expected.Path, actual.Path.WithoutFirstSlash());

            if (parentProduct == null)
            {
                Assert.Null(actual.Parent);
                Assert.Null(actual.RootProductName);
            }
            else
                Assert.Equal(parentProduct.Id, actual.Parent.Id);

            AssertModels(expected.BaseValue, actual.LastValue);
        }

        internal static void TestSensorModel(SensorUpdate expected, BaseSensorModel actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Integration, actual.Integration);
            Assert.Equal(expected.State, actual.State);
        }

        internal static void TestSensorModel(SensorUpdate expected, SensorEntity actual)
        {
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.State, (SensorState)actual.State);
            Assert.Equal(expected.Integration, (Integration)actual.Integration);
        }

        internal static void TestExpectedUpdateIntervalPolicy(SensorUpdate expected, DataPolicy actual)
        {
            //Assert.Equal(expected.TTL.Ticks, (actual as ExpectedUpdateIntervalPolicy).Interval.Ticks);
            //Assert.Equal(expected.TTL.Interval, (actual as ExpectedUpdateIntervalPolicy).Interval.Interval);
        }


        private static void TestSensorValue(BaseValue expected, BaseValue actual, SensorType type)
        {
            switch (type)
            {
                case SensorType.Boolean:
                    AssertSensorValues<BooleanValue>(expected, actual);
                    break;
                case SensorType.Integer:
                    AssertSensorValues<IntegerValue>(expected, actual);
                    break;
                case SensorType.Double:
                    AssertSensorValues<DoubleValue>(expected, actual);
                    break;
                case SensorType.String:
                    AssertSensorValues<StringValue>(expected, actual);
                    break;
                case SensorType.File:
                    AssertSensorValues<FileValue>(expected, actual);
                    break;
                case SensorType.IntegerBar:
                    AssertSensorValues<IntegerBarValue>(expected, actual);
                    break;
                case SensorType.DoubleBar:
                    AssertSensorValues<DoubleBarValue>(expected, actual);
                    break;
            }
        }

        private static void AssertSensorValues<T>(BaseValue actual, BaseValue expected) =>
            AssertModels((T)Convert.ChangeType(actual, typeof(T)), (T)Convert.ChangeType(expected, typeof(T)));

        private static BaseValue GetValue(byte[] valueBytes, SensorType type)
        {
            var value = type switch
            {
                SensorType.Boolean => valueBytes.ToValue<BooleanValue>(),
                SensorType.Integer => valueBytes.ToValue<IntegerValue>(),
                SensorType.Double => valueBytes.ToValue<DoubleValue>(),
                SensorType.String => valueBytes.ToValue<StringValue>(),
                SensorType.File => valueBytes.ToValue<FileValue>(),
                SensorType.IntegerBar => valueBytes.ToValue<IntegerBarValue>(),
                SensorType.DoubleBar => valueBytes.ToValue<DoubleBarValue>(),
                _ => null,
            };

            if (value is FileValue fileValue)
                value = fileValue.DecompressContent();

            return value;
        }

        private static void TestImmutableSensorData(BaseSensorModel expected, BaseSensorModel actual)
        {
            Assert.NotNull(actual.RootProductName);
            Assert.NotNull(actual.Path);
            Assert.NotNull(expected.RootProductName);
            Assert.NotNull(expected.Path);

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Parent.Id, actual.Parent.Id);
            Assert.Equal(expected.AuthorId, actual.AuthorId);
            Assert.Equal(expected.CreationDate, actual.CreationDate);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.RootProductName, actual.RootProductName);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Type, actual.Type);
            AssertModels(expected.Status, actual.Status);
        }

        private static void TestCollections<T>(List<T> expected, List<T> actual)
        {
            expected.Sort();
            actual.Sort();

            Assert.Equal(expected, actual);
        }
    }
}
