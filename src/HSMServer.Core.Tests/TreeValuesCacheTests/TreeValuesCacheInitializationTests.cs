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
        [Trait("Category", "Initialization")]
        public async void ProductsInitializationTest()
        {
            await Task.Delay(1000);

            var expectedProducts = _databaseCoreManager.DatabaseCore.GetAllProducts();
            var actualProducts = _treeValuesCache.GetTree();

            TestProducts(expectedProducts, actualProducts);
        }

        [Fact]
        [Trait("Category", "Initialization")]
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
                ModelsTester.TestProductModel(expectedProduct, actualDict[expectedProduct.Id]);
        }

        private void TestSensors(List<SensorEntity> expected, List<SensorModel> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            var actualDict = actual.ToDictionary(s => s.Id);
            foreach (var expectedSensor in expected)
            {
                var actualSensor = actualDict[Guid.Parse(expectedSensor.Id)];
                var expectedSensorData = _databaseCoreManager.DatabaseCore.GetLatestSensorValue(expectedSensor.ProductName, expectedSensor.Path);

                ModelsTester.TestSensorModel(expectedSensor, actualSensor);
                ModelsTester.TestSensorModel(expectedSensorData, actualSensor);
            }
        }


        private static (List<ProductEntity>, List<SensorEntity>, List<(string product, SensorDataEntity sensorData)>) GenerateTestData()
        {
            var products = new List<ProductEntity>(1 << 3);
            var sensors = new List<SensorEntity>(1 << 3);
            var sensorsData = new List<(string product, SensorDataEntity sensorData)>(1 << 3);

            for (int i = 0; i < 2; ++i)
            {
                var product = EntitiesFactory.BuildProductEntity($"product{i}", null);

                for (int j = 0; j < 2; j++)
                {
                    var subProduct = EntitiesFactory.BuildProductEntity($"subProduct{j}", product.Id);

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

                    product.AddSubProduct(subProduct.Id);
                    product.AddSensor(sensor.Id);

                    var subSubProduct = EntitiesFactory.BuildProductEntity($"subSubProduct", subProduct.Id);

                    subProduct.AddSubProduct(subSubProduct.Id);

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

                    subSubProduct.AddSensor(sensorForSubSubProduct.Id);

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
