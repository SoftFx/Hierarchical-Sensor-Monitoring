using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Converters;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class TreeValuesCacheTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>
    {
        private readonly TreeValuesCache _valuesCache;

        private delegate string GetProductNameById(Guid id);
        private delegate ProductModel GetProduct(Guid id);
        private delegate ProductEntity GetProductFromDb(string id);


        public TreeValuesCacheTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            InitializeDatabase();

            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);
        }


        [Fact]
        [Trait("Category", "Initialization")]
        public async void ProductsInitializationTest()
        {
            await Task.Delay(1000);

            var expectedProducts = _databaseCoreManager.DatabaseCore.GetAllProducts();
            var actualProducts = _valuesCache.GetTree();

            ModelsTester.TestProducts(expectedProducts, actualProducts);
        }

        [Fact]
        [Trait("Category", "Initialization")]
        public async void SensorsInitializationTest()
        {
            await Task.Delay(1000);

            var expectedSensors = _databaseCoreManager.DatabaseCore.GetAllSensors();
            var actualSensors = _valuesCache.GetSensors();

            TestSensors(expectedSensors, actualSensors);
        }


        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(1000)]
        [Trait("Category", "Add product(s)")]
        public async void AddProductsTest(int count)
        {
            int addedProductsCount = 0;
            int updatedProductsCount = 0;
            void AddProductEventHandle(ProductModel product, TransactionType type)
            {
                Assert.NotNull(product);

                if (type == TransactionType.Add)
                    addedProductsCount++;
                else
                    updatedProductsCount++;
            }


            await Task.Delay(100);

            var productNames = new List<string>(count);
            var addedProducts = new List<ProductModel>(count);

            _valuesCache.ChangeProductEvent += AddProductEventHandle;

            for (int i = 0; i < count; ++i)
            {
                var productName = RandomGenerator.GetRandomString();
                productNames.Add(productName);

                addedProducts.Add(_valuesCache.AddProduct(productName));
            }

            _valuesCache.ChangeProductEvent -= AddProductEventHandle;

            await Task.Delay(100);

            Assert.Equal(addedProductsCount, count);
            Assert.Equal(updatedProductsCount, count);
            for (int i = 0; i < count; ++i)
            {
                var productName = productNames[i];
                var product = addedProducts[i];

                Assert.Equal(productName, _valuesCache.GetProductNameById(product.Id));
                ModelsTester.TestProductModel(productName, product);

                ModelsTester.TestProductModel(product, _valuesCache.GetProduct(product.Id));
                ModelsTester.TestProductModel(_databaseCoreManager.GetProduct(product.Id), product);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [Trait("Category", "Remove product(s)")]
        public async void RemoveProductsTest(int count)
        {
            await Task.Delay(100);

            var addedProducts = new List<Guid>(count);
            for (int i = 0; i < count; ++i)
                addedProducts.Add(_valuesCache.AddProduct(RandomGenerator.GetRandomString()).Id);

            await Task.Delay(100);

            foreach (var productId in addedProducts)
                _valuesCache.RemoveProduct(productId);

            for (int i = 0; i < count; ++i)
                TestRemovedProduct(addedProducts[i],
                                   _valuesCache.GetProductNameById,
                                   _valuesCache.GetProduct,
                                   _databaseCoreManager.DatabaseCore.GetProduct);
        }

        [Fact]
        [Trait("Category", "Remove product(s)")]
        public async void RemoveProductTest()
        {
            int updatedProductsCount = 0;
            int deletedProductsCount = 0;
            void RemoveProductEventHandler(ProductModel product, TransactionType type)
            {
                Assert.NotNull(product);

                if (type == TransactionType.Update)
                    updatedProductsCount++;
                else
                    deletedProductsCount++;
            }

            int deletedSensorsCount = 0;
            void RemoveSensorEventHandler(BaseSensorModel sensor, TransactionType type)
            {
                Assert.NotNull(sensor);
                Assert.Equal(TransactionType.Delete, type);

                deletedSensorsCount++;
            }


            await Task.Delay(100);

            var product = GetProductByName("subProduct0_product0");
            var parentProduct = product.ParentProduct;

            var expectedDeletedProductIds = GetAllProductIdsInBranch(product);
            var expectedDeletedSensorIds = GetAllSensorIdsInBranch(product);

            _valuesCache.ChangeProductEvent += RemoveProductEventHandler;
            _valuesCache.ChangeSensorEvent += RemoveSensorEventHandler;

            _valuesCache.RemoveProduct(product.Id);

            _valuesCache.ChangeProductEvent -= RemoveProductEventHandler;
            _valuesCache.ChangeSensorEvent -= RemoveSensorEventHandler;

            Assert.Equal(deletedSensorsCount, expectedDeletedSensorIds.Count);
            Assert.Equal(deletedProductsCount, expectedDeletedProductIds.Count);
            foreach (var productId in expectedDeletedProductIds)
                TestRemovedProduct(productId,
                                   _valuesCache.GetProductNameById,
                                   _valuesCache.GetProduct,
                                   _databaseCoreManager.DatabaseCore.GetProduct);

            Assert.Equal(1, updatedProductsCount);
            Assert.DoesNotContain(product.Id, parentProduct.SubProducts.Keys);
        }

        [Fact]
        [Trait("Category", "Products without parent")]
        public async void GetProductsWithoutParentTest()
        {
            var actualProducts = _valuesCache.GetProducts(null);

            await TestProductsWithoutParent(actualProducts);
        }

        [Fact]
        [Trait("Category", "Products without parent")]
        public async void GetProductsWithoutParent_Admin_Test()
        {
            var actualProducts = _valuesCache.GetProducts(TestUsersManager.Admin);

            await TestProductsWithoutParent(actualProducts);
        }

        [Fact]
        [Trait("Category", "Products without parent")]
        public void GetProductsWithoutParent_UserWithoutProductRoles_Test()
        {
            var actualProducts = _valuesCache.GetProducts(TestUsersManager.NotAdmin);

            Assert.Empty(actualProducts);
        }

        [Theory]
        [InlineData(ProductRoleEnum.ProductManager)]
        [InlineData(ProductRoleEnum.ProductViewer)]
        [Trait("Category", "Products without parent")]
        public void GetProductsWithoutParent_ProductManagerViewer_Test(ProductRoleEnum productRole)
        {
            var selfMonitoringProductManager =
                TestUsersManager.BuildUserWithRole(productRole, TestProductsManager.TestProduct.Id);

            var actualProducts = _valuesCache.GetProducts(selfMonitoringProductManager);

            Assert.Single(actualProducts);

            var actualProduct = actualProducts.First();
            Assert.Equal(TestProductsManager.ProductId, actualProduct.Id);
            ModelsTester.TestProductModel(TestProductsManager.ProductName, actualProduct);
        }

        [Fact]
        [Trait("Category", "Get product name")]
        public void GetProductName_NonExistingId_Test()
        {
            var productName = _valuesCache.GetProductNameById(Guid.NewGuid());

            Assert.Null(productName);
        }


        [Fact]
        [Trait("Category", "Get sensor (convert to Entity and back)")]
        public void CloneSensorTest()
        {
            var sensor = GetSensorByNameFromCache("sensor0");

            var clonedSensor = GetClonedSensorModel(sensor);

            ModelsTester.AssertModels(sensor, clonedSensor);
            ModelsTester.AssertModels(sensor.ValidationResult, clonedSensor.ValidationResult);
        }

        [Fact]
        [Trait("Category", "Update sensor(s)")]
        public void UpdateSensorsTest()
        {
            var sensor = GetClonedSensorModel(GetSensorByNameFromCache("sensor0"));
            var sensorUpdate = SensorModelFactory.BuildSensorUpdate(sensor.Id);

            int updatedSensorsCount = 0;
            void UpdateSensorEventHandler(BaseSensorModel updatedSensor, TransactionType type)
            {
                Assert.NotNull(updatedSensor);
                Assert.Equal(TransactionType.Update, type);

                ModelsTester.TestSensorModel(sensorUpdate, updatedSensor);

                updatedSensorsCount++;
            }

            _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

            _valuesCache.UpdateSensor(sensorUpdate);

            _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

            Assert.Equal(1, updatedSensorsCount);
            TestSensorUpdates(sensor, sensorUpdate);
        }

        [Fact]
        [Trait("Category", "Update sensor(s)")]
        public void UpdateAllSensorsTest()
        {
            var allSensors = _valuesCache.GetSensors();

            var sensorUpdates = new List<(BaseSensorModel sensor, SensorUpdate update)>(allSensors.Count);
            foreach (var sensor in allSensors)
                sensorUpdates.Add((GetClonedSensorModel(sensor), SensorModelFactory.BuildSensorUpdate(sensor.Id)));

            int updatedSensorsCount = 0;
            void UpdateSensorEventHandler(BaseSensorModel updatedSensor, TransactionType type)
            {
                Assert.NotNull(updatedSensor);
                Assert.Equal(TransactionType.Update, type);

                updatedSensorsCount++;
            }

            _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

            foreach (var (_, sensorUpdate) in sensorUpdates)
                _valuesCache.UpdateSensor(sensorUpdate);

            _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

            Assert.Equal(sensorUpdates.Count, updatedSensorsCount);
            foreach (var (sensor, sensorUpdate) in sensorUpdates)
                TestSensorUpdates(sensor, sensorUpdate);
        }

        [Fact]
        [Trait("Category", "Remove sensor(s)")]
        public async Task RemoveSensorsTest()
        {
            var allSensors = _valuesCache.GetSensors();

            int removedSensorsCount = 0;
            void RemoveSensorEventHandler(BaseSensorModel removedSensor, TransactionType type)
            {
                Assert.NotNull(removedSensor);
                Assert.Equal(TransactionType.Delete, type);

                removedSensorsCount++;
            }

            _valuesCache.ChangeSensorEvent += RemoveSensorEventHandler;

            foreach (var sensor in allSensors)
                _valuesCache.RemoveSensor(sensor.Id);

            _valuesCache.ChangeSensorEvent -= RemoveSensorEventHandler;

            Assert.Equal(allSensors.Count, removedSensorsCount);
            Assert.Empty(_valuesCache.GetSensors());
            Assert.Empty(_databaseCoreManager.DatabaseCore.GetAllSensors());
            foreach (var sensor in allSensors)
                Assert.Empty(await GetAllSensorValues(sensor));
        }

        [Fact]
        [Trait("Category", "Remove sensor(s) data")]
        public async Task RemoveSensorsDataTest()
        {
            int clearedSensorsCount = 0;
            void UpdateSensorEventHandler(BaseSensorModel clearedSensor, TransactionType type)
            {
                Assert.NotNull(clearedSensor);
                Assert.Equal(TransactionType.Update, type);

                clearedSensorsCount++;
            }


            var product = GetProductByName("product0");
            var sensors = GetAllSensorIdsInBranch(product);

            _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

            _valuesCache.RemoveSensorsData(product.Id);

            _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

            Assert.Equal(4, clearedSensorsCount);
            foreach (var sensorId in sensors)
                await TestClearedSensor(sensorId);
        }

        [Fact]
        [Trait("Category", "Remove sensor(s) data")]
        public async Task RemoveSensorDataTest()
        {
            var sensor = GetSensorByNameFromCache("sensor0");

            _valuesCache.RemoveSensorData(sensor.Id);

            await TestClearedSensor(sensor.Id);
            ModelsTester.TestSensorDataWithoutClearedData(sensor, GetSensorByIdFromCache(sensor.Id));
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Add new sensor value")]
        public void AddNewSensorValueTest(SensorType type)
        {
            int addedProductsCount = 0;
            int updatedProductsCount = 0;
            void ChangeProductEventHandler(ProductModel product, TransactionType type)
            {
                Assert.NotNull(product);

                if (type == TransactionType.Add)
                    addedProductsCount++;
                else if (type == TransactionType.Update)
                    updatedProductsCount++;
            }

            int addedSensorsCount = 0;
            int updatedSensorsCount = 0;
            void ChangeSensorEventHandler(BaseSensorModel sensor, TransactionType type)
            {
                Assert.NotNull(sensor);

                if (type == TransactionType.Add)
                    addedSensorsCount++;
                else if (type == TransactionType.Update)
                    updatedSensorsCount++;
            }


            string accessKey = TestProductsManager.TestProductKey.Id;
            Guid productId = TestProductsManager.ProductId;

            const string subProductName = "new_subProduct";
            const string subSubProductName = "new_subSubProduct";
            const string sensorName = "new_sensor";

            var storeInfo = BuildSensorStoreInfo(accessKey, $"{subProductName}/{subSubProductName}/{sensorName}", type);

            _valuesCache.ChangeProductEvent += ChangeProductEventHandler;
            _valuesCache.ChangeSensorEvent += ChangeSensorEventHandler;

            _valuesCache.AddNewSensorValue(storeInfo);

            _valuesCache.ChangeProductEvent -= ChangeProductEventHandler;
            _valuesCache.ChangeSensorEvent -= ChangeSensorEventHandler;

            var product = _valuesCache.GetProduct(productId);
            var subProduct = GetProductByName(subProductName);
            var subSubProduct = GetProductByName(subSubProductName);
            var sensor = GetSensorByNameFromCache(sensorName);
            var sensorDataFromDb = _databaseCoreManager.DatabaseCore.GetLatestValues(new(1) { sensor }).FirstOrDefault().Value;

            Assert.Equal(2, addedProductsCount);
            Assert.Equal(5, updatedProductsCount);
            Assert.NotEmpty(product.SubProducts);

            ModelsTester.TestProductModel(subProductName, subProduct, parentProduct: product, subProducts: new List<ProductModel>() { subSubProduct });
            ModelsTester.TestProductModel(subSubProductName, subSubProduct, parentProduct: subProduct, sensors: new List<BaseSensorModel>() { sensor });
            ModelsTester.TestProductModel(_databaseCoreManager.GetProduct(product.Id), product);
            ModelsTester.TestProductModel(_databaseCoreManager.GetProduct(subProduct.Id), subProduct);
            ModelsTester.TestProductModel(_databaseCoreManager.GetProduct(subSubProduct.Id), subSubProduct);

            Assert.Equal(1, addedSensorsCount);
            Assert.Equal(1, updatedSensorsCount);

            ModelsTester.TestSensorModel(storeInfo, sensor, parentProduct: subSubProduct);
            ModelsTester.TestSensorModel(GetSensorByIdFromDb(sensor.Id), sensor);

            if (sensor is IBarSensor barSensor)
            {
                Assert.Null(sensorDataFromDb);
                Assert.True(sensor.HasData);
                ModelsTester.AssertModels(sensor.LastValue, barSensor.LocalLastValue);
            }
            else
                ModelsTester.TestSensorModel(sensorDataFromDb, sensor);
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Add new sensor value")]
        public void AddNewSensorValue_UpdateData_Test(SensorType type)
        {
            int updatedSensorsCount = 0;
            void ChangeSensorEventHandler(BaseSensorModel sensor, TransactionType type)
            {
                Assert.NotNull(sensor);
                Assert.Equal(TransactionType.Update, type);

                updatedSensorsCount++;
            }


            string accessKey = TestProductsManager.TestProductKey.Id;
            Guid productId = TestProductsManager.ProductId;

            const string sensorName = "new_sensor";

            var timeCollected = DateTime.UtcNow;

            var storeInfo = BuildSensorStoreInfo(accessKey, $"{sensorName}", type);
            _valuesCache.AddNewSensorValue(storeInfo);
            var sensorWithFirstValue = GetClonedSensorModel(GetSensorByNameFromCache(sensorName));

            _valuesCache.ChangeSensorEvent += ChangeSensorEventHandler;

            storeInfo = BuildSensorStoreInfo(accessKey, $"{sensorName}", type);
            _valuesCache.AddNewSensorValue(storeInfo);

            _valuesCache.ChangeSensorEvent -= ChangeSensorEventHandler;

            var product = _valuesCache.GetProduct(productId);
            var sensor = GetSensorByNameFromCache(sensorName);
            var sensorDataFromDb = _databaseCoreManager.DatabaseCore.GetLatestValues(new(1) { sensor }).FirstOrDefault().Value;

            Assert.Equal(1, updatedSensorsCount);
            Assert.NotEmpty(product.Sensors);

            ModelsTester.TestSensorModel(storeInfo, sensor, parentProduct: product);
            ModelsTester.TestSensorModel(GetSensorByIdFromDb(sensor.Id), sensor);

            if (sensor is IBarSensor)
                ModelsTester.TestSensorModel(sensorDataFromDb, sensorWithFirstValue);
            else
                ModelsTester.TestSensorModel(sensorDataFromDb, sensor);
        }


        private async Task TestProductsWithoutParent(List<ProductModel> actualProducts)
        {
            await Task.Delay(100);

            var expectedProducts = _databaseCoreManager.DatabaseCore.GetAllProducts().Where(p => p.ParentProductId == null).ToList();

            ModelsTester.TestProducts(expectedProducts, actualProducts);
        }

        private ProductModel GetProductByName(string name) =>
            _valuesCache.GetTree().FirstOrDefault(p => p.DisplayName == name);

        private BaseSensorModel GetSensorByNameFromCache(string name) =>
            _valuesCache.GetSensors().FirstOrDefault(s => s.DisplayName == name);

        private BaseSensorModel GetSensorByIdFromCache(Guid id) => _valuesCache.GetSensor(id);

        private SensorEntity GetSensorByIdFromDb(Guid id) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().FirstOrDefault(s => s.Id == id.ToString());

        private ValueTask<List<byte[]>> GetAllSensorValues(BaseSensorModel sensor) =>
            _databaseCoreManager.DatabaseCore.GetSensorValuesPage(sensor.Id.ToString(), DateTime.MinValue, DateTime.MaxValue, MaxHistoryCount).JoinAllPages();


        private void TestSensors(List<SensorEntity> expected, List<BaseSensorModel> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            var expectedSensorValues = _databaseCoreManager.DatabaseCore.GetLatestValues(actual);
            var actualDict = actual.ToDictionary(s => s.Id);

            foreach (var expectedSensor in expected)
            {
                var sensorId = Guid.Parse(expectedSensor.Id);
                var actualSensor = actualDict[sensorId];

                ModelsTester.TestSensorModel(expectedSensor, actualSensor);
                ModelsTester.TestSensorModel(expectedSensorValues[sensorId], actualSensor);
            }
        }

        private void TestSensorUpdates(BaseSensorModel sensor, SensorUpdate sensorUpdate)
        {
            var actualSensorFromCache = GetSensorByIdFromCache(sensor.Id);
            var actualSensorFromDb = GetSensorByIdFromDb(sensor.Id);

            ModelsTester.TestSensorModel(sensorUpdate, actualSensorFromCache);
            ModelsTester.TestSensorModel(sensorUpdate, actualSensorFromDb);
            ModelsTester.TestSensorModelWithoutUpdatedMetadata(actualSensorFromCache, sensor);
            ModelsTester.TestSensorModelWithoutUpdatedMetadata(actualSensorFromDb, sensor);

            var actualExpectedUpdateIntervalPolicy = GetPolicyByIdFromDb(actualSensorFromCache.ExpectedUpdateInterval.Id);

            ModelsTester.TestExpectedUpdateIntervalPolicy(sensorUpdate, actualExpectedUpdateIntervalPolicy);
            ModelsTester.AssertModels(actualSensorFromCache.ExpectedUpdateInterval, actualExpectedUpdateIntervalPolicy);
        }

        private Policy GetPolicyByIdFromDb(Guid id)
        {
            var policyEntities = _databaseCoreManager.DatabaseCore.GetAllPolicies();

            var serializeOptions = new JsonSerializerOptions();
            serializeOptions.Converters.Add(new PolicyDeserializationConverter());

            foreach (var entity in policyEntities)
            {
                var policy = JsonSerializer.Deserialize<Policy>(entity, serializeOptions);
                if (policy.Id == id)
                    return policy;
            }

            return null;
        }

        private BaseSensorModel GetClonedSensorModel(BaseSensorModel sensor)
        {
            var clonedSensor = SensorModelFactory.Build(sensor.ToEntity());
            clonedSensor.ParentProduct = _valuesCache.GetProduct(sensor.ParentProduct.Id);
            clonedSensor.BuildProductNameAndPath();

            clonedSensor.TryAddValue(sensor.LastValue, out _);

            return clonedSensor;
        }

        private async Task TestClearedSensor(Guid clearedSensorId)
        {
            var sensor = GetSensorByIdFromCache(clearedSensorId);

            Assert.NotNull(sensor);
            Assert.Equal(DateTime.MinValue, sensor.LastUpdateTime);
            Assert.Equal(default, sensor.LastValue);
            Assert.False(sensor.HasData);

            Assert.Empty(await GetAllSensorValues(sensor));
            Assert.NotNull(GetSensorByIdFromDb(clearedSensorId));
        }

        private static void TestRemovedProduct(Guid productId, GetProductNameById getProductName,
        GetProduct getProduct, GetProductFromDb getProductFromDb)
        {
            Assert.Null(getProductName?.Invoke(productId));
            Assert.Null(getProduct?.Invoke(productId));
            Assert.Null(getProductFromDb?.Invoke(productId.ToString()));
        }


        private static List<Guid> GetAllProductIdsInBranch(ProductModel model)
        {
            var products = new List<Guid>(1 << 2);

            void AddSubProductsToList(ProductModel product)
            {
                foreach (var (_, subProduct) in product.SubProducts)
                    AddSubProductsToList(subProduct);

                products.Add(product.Id);
            }

            AddSubProductsToList(model);

            return products;
        }

        private static List<Guid> GetAllSensorIdsInBranch(ProductModel model)
        {
            var sensors = new List<Guid>(1 << 2);

            void AddSensorsToList(ProductModel product)
            {
                foreach (var (_, subProduct) in product.SubProducts)
                    AddSensorsToList(subProduct);

                foreach (var (_, sensor) in product.Sensors)
                    sensors.Add(sensor.Id);
            }

            AddSensorsToList(model);

            return sensors;
        }

        private static StoreInfo BuildSensorStoreInfo(string key, string path, SensorType type) =>
            new(key, path)
            {
                BaseValue = SensorValuesFactory.BuildSensorValue(type)
            };

        private void InitializeDatabase()
        {
            var (products, sensors, sensorValues) = GenerateTestData();

            foreach (var product in products)
                _databaseCoreManager.DatabaseCore.AddProduct(product);

            foreach (var sensor in sensors)
                _databaseCoreManager.DatabaseCore.AddSensor(sensor);

            foreach (var sensorValue in sensorValues)
                _databaseCoreManager.DatabaseCore.AddSensorValue(sensorValue);
        }

        private static (List<ProductEntity>, List<SensorEntity>, List<SensorValueEntity>) GenerateTestData()
        {
            var products = new List<ProductEntity>(1 << 3);
            var sensors = new List<SensorEntity>(1 << 3);
            var sensorValues = new List<SensorValueEntity>(1 << 3);

            for (int i = 0; i < 2; ++i)
            {
                var product = EntitiesFactory.BuildProductEntity($"product{i}", null);

                for (int j = 0; j < 2; j++)
                {
                    var subProduct = EntitiesFactory.BuildProductEntity($"subProduct{j}_{product.DisplayName}", product.Id);

                    var sensor = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = product.Id,
                        DisplayName = $"sensor{j}",
                        Type = (byte)SensorType.Boolean,
                    };
                    var boolValue = new BooleanValue()
                    {
                        Time = DateTime.UtcNow,
                        Status = SensorStatus.Warning,
                        Comment = "sensorValue",
                        Value = true,
                    };
                    var sensorValue = new SensorValueEntity()
                    {
                        SensorId = sensor.Id,
                        ReceivingTime = boolValue.ReceivingTime.Ticks,
                        Value = boolValue,
                    };

                    var subSubProduct = EntitiesFactory.BuildProductEntity($"subSubProduct", subProduct.Id);

                    var sensorForSubSubProduct = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = subSubProduct.Id,
                        DisplayName = "sensor",
                        Type = (int)SensorType.Integer,
                    };
                    var intValue = new IntegerValue()
                    {
                        Time = DateTime.UtcNow,
                        Status = SensorStatus.Ok,
                        Comment = "sensorValue1",
                        Value = 12345,
                    };
                    var sensorForSubSubProductValue = new SensorValueEntity()
                    {
                        SensorId = sensorForSubSubProduct.Id,
                        ReceivingTime = intValue.ReceivingTime.Ticks,
                        Value = intValue,
                    };

                    products.Add(subProduct);
                    products.Add(subSubProduct);

                    sensors.Add(sensor);
                    sensors.Add(sensorForSubSubProduct);

                    sensorValues.Add(sensorValue);
                    sensorValues.Add(sensorForSubSubProductValue);
                }

                products.Add(product);
            }

            return (products, sensors, sensorValues);
        }
    }
}
