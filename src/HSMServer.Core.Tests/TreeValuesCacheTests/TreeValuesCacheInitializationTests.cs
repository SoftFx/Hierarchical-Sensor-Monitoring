using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    [Collection("Database collection")]
    public class TreeValuesCacheInitializationTests : IClassFixture<TreeValuesCacheFixture>
    {
        private readonly DatabaseCoreManager _databaseCoreManager;
        private readonly ITreeValuesCache _treeValuesCache;


        public TreeValuesCacheInitializationTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
        {
            _databaseCoreManager = new DatabaseCoreManager(fixture.DatabasePath);
            registerFixture.RegisterDatabase(_databaseCoreManager);

            InitializeDatabase();

            var userManager = new UserManager(_databaseCoreManager.DatabaseCore, CommonMoqs.CreateNullLogger<UserManager>());
            _treeValuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, userManager);
        }


        [Fact]
        [Trait("Category", "Products initialization")]
        public async void ProductsInitializationTest()
        {
            await Task.Delay(1000);

            var expectedProducts = _databaseCoreManager.DatabaseCore.GetAllProducts();
            var actualProducts = _treeValuesCache.GetTree();

            TestProducts(expectedProducts, actualProducts);
        }

        [Fact]
        [Trait("Category", "Sensors initialization")]
        public async void SensorsInitializationTest()
        {
            await Task.Delay(1000);

            var expectedSensors = _databaseCoreManager.DatabaseCore.GetAllSensors();
            var actualSensors = _treeValuesCache.GetSensors();

            TestSensors(expectedSensors, actualSensors);
        }


        private void InitializeDatabase()
        {
            var (products, sensors, sensorsData) = GenerateTestData();

            foreach (var product in products)
                _databaseCoreManager.DatabaseCore.AddProduct(product);

            foreach (var sensor in sensors)
                _databaseCoreManager.DatabaseCore.AddSensor(sensor);

            foreach (var sensorData in sensorsData)
                _databaseCoreManager.DatabaseCore.PutSensorData(sensorData.sensorData, sensorData.product);
        }


        private static void TestProducts(List<ProductEntity> expected, List<ProductModel> actual)
        {
            var actualDict = actual.ToDictionary(p => p.Id);

            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedProduct in expected)
            {
                var actualProduct = actualDict[expectedProduct.Id];

                Assert.NotNull(actualProduct);
                Assert.Equal(expectedProduct.Id, actualProduct.Id);
                Assert.Equal(expectedProduct.ParentProductId, actualProduct.ParentProduct?.Id);
                Assert.Equal(expectedProduct.DisplayName, actualProduct.DisplayName);
                Assert.Equal(expectedProduct.State, (int)actualProduct.State);
                Assert.Equal(expectedProduct.Description, actualProduct.Description);
                Assert.Equal(expectedProduct.CreationDate, actualProduct.CreationDate.Ticks);

                var actualSubProducts = actualProduct.SubProducts.Select(p => p.Key).ToList();
                var expectedSubProducts = expectedProduct.SubProductsIds;
                expectedSubProducts.Sort();
                actualSubProducts.Sort();

                Assert.Equal(expectedSubProducts, actualSubProducts);

                var actualSensors = actualProduct.Sensors.Select(p => p.Key.ToString()).ToList();
                var expectedSensors = expectedProduct.SensorsIds;
                expectedSensors.Sort();
                actualSensors.Sort();

                Assert.Equal(expectedSensors, actualSensors);
            }
        }

        private void TestSensors(List<SensorEntity> expected, List<SensorModel> actual)
        {
            long GetTimestamp(DateTime dateTime)
            {
                var timeSpan = dateTime - DateTime.UnixEpoch;
                return (long)timeSpan.TotalSeconds;
            }


            var actualDict = actual.ToDictionary(s => s.Id);

            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedSensor in expected)
            {
                var actualSensor = actualDict[Guid.Parse(expectedSensor.Id)];

                Assert.NotNull(actual);
                Assert.Equal(expectedSensor.Id, actualSensor.Id.ToString());
                Assert.Equal(expectedSensor.ProductId, actualSensor.ParentProduct.Id);
                Assert.Equal(expectedSensor.SensorName, actualSensor.SensorName);
                Assert.Equal(expectedSensor.ProductName, actualSensor.ProductName);
                Assert.Equal(expectedSensor.Path, actualSensor.Path);
                Assert.Equal(expectedSensor.Description, actualSensor.Description);
                Assert.Equal(expectedSensor.ExpectedUpdateIntervalTicks, actualSensor.ExpectedUpdateInterval.Ticks);
                Assert.Equal(expectedSensor.Unit, actualSensor.Unit);
                Assert.Equal(expectedSensor.SensorType, (int)actualSensor.SensorType);
                Assert.True(string.IsNullOrEmpty(actualSensor.ValidationError));

                var expectedSensorData = _databaseCoreManager.DatabaseCore.GetLatestSensorValue(expectedSensor.ProductName, expectedSensor.Path);

                Assert.NotNull(expectedSensorData);
                Assert.Equal(expectedSensorData.Path, actualSensor.Path);
                Assert.Equal(expectedSensorData.DataType, (byte)actualSensor.SensorType);
                Assert.Equal(expectedSensorData.Time, actualSensor.SensorTime);
                if (expectedSensorData.Timestamp != 0)
                    Assert.Equal(expectedSensorData.Timestamp, GetTimestamp(actualSensor.SensorTime));
                Assert.Equal(expectedSensorData.TimeCollected, actualSensor.LastUpdateTime);
                Assert.Equal(expectedSensorData.Status, (byte)actualSensor.Status);
                Assert.Equal(expectedSensorData.TypedData, actualSensor.TypedData);
                Assert.Equal(expectedSensorData.OriginalFileSensorContentSize, actualSensor.OriginalFileSensorContentSize);
            }
        }


        private static (List<ProductEntity>, List<SensorEntity>, List<(string product, SensorDataEntity sensorData)>) GenerateTestData()
        {
            var products = new List<ProductEntity>(1 << 3);
            var sensors = new List<SensorEntity>(1 << 3);
            var sensorsData = new List<(string product, SensorDataEntity sensorData)>(1 << 3);

            for (int i = 0; i < 2; ++i)
            {
                var product = new ProductEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    State = (int)ProductState.FullAccess,
                    DisplayName = $"product{i}",
                    CreationDate = DateTime.UtcNow.Ticks,
                    SubProductsIds = new List<string>(1 << 1),
                    SensorsIds = new List<string>(1 << 1),
                };

                for (int j = 0; j < 2; j++)
                {
                    var subProduct = new ProductEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentProductId = product.Id,
                        State = (int)ProductState.FullAccess,
                        DisplayName = $"subProduct{j}",
                        CreationDate = DateTime.UtcNow.Ticks,
                        SubProductsIds = new List<string>(1 << 1),
                        SensorsIds = new List<string>(0),
                    };

                    var sensor = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = product.Id,
                        ProductName = product.DisplayName,
                        Path = $"{product.DisplayName}/sensor{j}",
                        SensorName = $"sensor{j}",
                        SensorType = (int)SensorType.BooleanSensor,
                    };
                    var sensorData = new SensorDataEntity()
                    {
                        Path = sensor.Path,
                        TimeCollected = DateTime.UtcNow,
                        DataType = (byte)sensor.SensorType,
                        TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = true, Comment = "sensorData" }),
                        Status = (byte)SensorStatus.Warning,
                    };

                    product.SubProductsIds.Add(subProduct.Id);
                    product.SensorsIds.Add(sensor.Id);

                    var subSubProduct = new ProductEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentProductId = subProduct.Id,
                        State = (int)ProductState.FullAccess,
                        DisplayName = $"subSubProduct",
                        CreationDate = DateTime.UtcNow.Ticks,
                        SubProductsIds = new List<string>(0),
                        SensorsIds = new List<string>(1 << 1),
                    };

                    subProduct.SubProductsIds.Add(subSubProduct.Id);

                    var sensorForSubSubProduct = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = subSubProduct.Id,
                        ProductName = subSubProduct.DisplayName,
                        Path = $"{product.DisplayName}{subProduct.DisplayName}{subSubProduct.DisplayName}/sensor",
                        SensorName = "sensor",
                        SensorType = (int)SensorType.IntSensor,
                    };
                    var sensorForSubSubProductData = new SensorDataEntity()
                    {
                        Path = sensorForSubSubProduct.Path,
                        TimeCollected = DateTime.UtcNow,
                        DataType = (byte)sensorForSubSubProduct.SensorType,
                        TypedData = JsonSerializer.Serialize(new IntSensorData() { IntValue = 12345, Comment = "sensorData1" }),
                        Status = (byte)SensorStatus.Ok,
                    };

                    subSubProduct.SensorsIds.Add(sensorForSubSubProduct.Id);

                    products.Add(subProduct);
                    products.Add(subSubProduct);

                    sensors.Add(sensor);
                    sensors.Add(sensorForSubSubProduct);

                    sensorsData.Add((product.DisplayName, sensorData));
                    sensorsData.Add((subSubProduct.DisplayName, sensorForSubSubProductData));
                }

                products.Add(product);
            }

            return (products, sensors, sensorsData);
        }
    }
}
