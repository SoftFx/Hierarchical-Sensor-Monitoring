using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class TreeValuesCacheTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>
    {
        private readonly TreeValuesCache _valuesCache;

        private delegate string GetProductNameById(string id);
        private delegate ProductModel GetProduct(string id);
        private delegate ProductEntity GetProductFromDb(string id);


        public TreeValuesCacheTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            InitializeDatabase();

            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);
        }


        [Fact]
        [Trait("Category", "Initialization")]
        public async void ProductsInitialization_SelfMonitoringProduct_Test()
        {
            await Task.Delay(100);

            var product = _valuesCache.GetProduct(CommonConstants.SelfMonitoringProductKey);

            ModelsTester.TestProductModel(CommonConstants.SelfMonitoringProductName, product);
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

        //[Fact]
        //[Trait("Category", "Initialization")]
        //public async void SensorsInitializationTest()
        //{
        //    await Task.Delay(1000);

        //    var expectedSensors = _databaseCoreManager.DatabaseCore.GetAllSensors();
        //    var actualSensors = _valuesCache.GetSensors();

        //    TestSensors(expectedSensors, actualSensors);
        //}


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
                ModelsTester.TestProductModel(_databaseCoreManager.DatabaseCore.GetProduct(product.Id), product);
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

            var addedProducts = new List<string>(count);
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

        //[Fact]
        //[Trait("Category", "Remove product(s)")]
        //public async void RemoveProductTest()
        //{
        //    int updatedProductsCount = 0;
        //    int deletedProductsCount = 0;
        //    void RemoveProductEventHandler(ProductModel product, TransactionType type)
        //    {
        //        Assert.NotNull(product);

        //        if (type == TransactionType.Update)
        //            updatedProductsCount++;
        //        else
        //            deletedProductsCount++;
        //    }

        //    int deletedSensorsCount = 0;
        //    void RemoveSensorEventHandler(SensorModel sensor, TransactionType type)
        //    {
        //        Assert.NotNull(sensor);
        //        Assert.Equal(TransactionType.Delete, type);

        //        deletedSensorsCount++;
        //    }


        //    await Task.Delay(100);

        //    var product = GetProductByName("subProduct0_product0");
        //    var parentProduct = product.ParentProduct;

        //    var expectedDeletedProductIds = GetAllProductIdsInBranch(product);
        //    var expectedDeletedSensorIds = GetAllSensorIdsInBranch(product);

        //    _valuesCache.ChangeProductEvent += RemoveProductEventHandler;
        //    _valuesCache.ChangeSensorEvent += RemoveSensorEventHandler;

        //    _valuesCache.RemoveProduct(product.Id);

        //    _valuesCache.ChangeProductEvent -= RemoveProductEventHandler;
        //    _valuesCache.ChangeSensorEvent -= RemoveSensorEventHandler;

        //    Assert.Equal(deletedSensorsCount, expectedDeletedSensorIds.Count);
        //    Assert.Equal(deletedProductsCount, expectedDeletedProductIds.Count);
        //    foreach (var productId in expectedDeletedProductIds)
        //        TestRemovedProduct(productId,
        //                           _valuesCache.GetProductNameById,
        //                           _valuesCache.GetProduct,
        //                           _databaseCoreManager.DatabaseCore.GetProduct);

        //    Assert.Equal(1, updatedProductsCount);
        //    Assert.DoesNotContain(product.Id, parentProduct.SubProducts.Keys);
        //}

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
                TestUsersManager.BuildUserWithRole(productRole, CommonConstants.SelfMonitoringProductKey);

            var actualProducts = _valuesCache.GetProducts(selfMonitoringProductManager);

            Assert.Single(actualProducts);

            var actualProduct = actualProducts.First();
            Assert.Equal(CommonConstants.SelfMonitoringProductKey, actualProduct.Id);
            ModelsTester.TestProductModel(CommonConstants.SelfMonitoringProductName, actualProduct);
        }

        [Fact]
        [Trait("Category", "Get product name")]
        public void GetProductName_NonExistingId_Test()
        {
            var productName = _valuesCache.GetProductNameById(RandomGenerator.GetRandomString());

            Assert.Null(productName);
        }


        //[Fact]
        //[Trait("Category", "Get sensor (convert to Entity and back)")]
        //public void CloneSensorTest()
        //{
        //    var sensor = GetSensorByNameFromCache("sensor0");

        //    var clonedSensor = GetClonedSensorModel(sensor);

        //    ModelsTester.TestSensorModel(sensor, clonedSensor);
        //}

        //[Fact]
        //[Trait("Category", "Update sensor(s)")]
        //public void UpdateSensorsTest()
        //{
        //    var sensor = GetClonedSensorModel(GetSensorByNameFromCache("sensor0"));
        //    var sensorUpdate = BuildSensorUpdate(sensor.Id);

        //    int updatedSensorsCount = 0;
        //    void UpdateSensorEventHandler(SensorModel updatedSensor, TransactionType type)
        //    {
        //        Assert.NotNull(updatedSensor);
        //        Assert.Equal(TransactionType.Update, type);

        //        ModelsTester.TestSensorModel(sensorUpdate, updatedSensor);

        //        updatedSensorsCount++;
        //    }

        //    _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

        //    _valuesCache.UpdateSensor(sensorUpdate);

        //    _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

        //    Assert.Equal(1, updatedSensorsCount);
        //    TestSensoUpdates(sensor, sensorUpdate);
        //}

        //[Fact]
        //[Trait("Category", "Update sensor(s)")]
        //public void UpdateAllSensorsTest()
        //{
        //    var allSensors = _valuesCache.GetSensors();

        //    var sensorUpdates = new List<(SensorModel sensor, SensorUpdate update)>(allSensors.Count);
        //    foreach (var sensor in allSensors)
        //        sensorUpdates.Add((GetClonedSensorModel(sensor), BuildSensorUpdate(sensor.Id)));

        //    int updatedSensorsCount = 0;
        //    void UpdateSensorEventHandler(SensorModel updatedSensor, TransactionType type)
        //    {
        //        Assert.NotNull(updatedSensor);
        //        Assert.Equal(TransactionType.Update, type);

        //        updatedSensorsCount++;
        //    }

        //    _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

        //    foreach (var (_, sensorUpdate) in sensorUpdates)
        //        _valuesCache.UpdateSensor(sensorUpdate);

        //    _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

        //    Assert.Equal(sensorUpdates.Count, updatedSensorsCount);
        //    foreach (var (sensor, sensorUpdate) in sensorUpdates)
        //        TestSensoUpdates(sensor, sensorUpdate);
        //}

        //[Fact]
        //[Trait("Category", "Remove sensor(s)")]
        //public void RemoveSensorsTest()
        //{
        //    var allSensors = _valuesCache.GetSensors();

        //    int removedSensorsCount = 0;
        //    void RemoveSensorEventHandler(SensorModel removedSensor, TransactionType type)
        //    {
        //        Assert.NotNull(removedSensor);
        //        Assert.Equal(TransactionType.Delete, type);

        //        removedSensorsCount++;
        //    }

        //    _valuesCache.ChangeSensorEvent += RemoveSensorEventHandler;

        //    foreach (var sensor in allSensors)
        //        _valuesCache.RemoveSensor(sensor.Id);

        //    _valuesCache.ChangeSensorEvent -= RemoveSensorEventHandler;

        //    Assert.Equal(allSensors.Count, removedSensorsCount);
        //    Assert.Empty(_valuesCache.GetSensors());
        //    Assert.Empty(_databaseCoreManager.DatabaseCore.GetAllSensors());
        //    foreach (var sensor in allSensors)
        //        Assert.Empty(_databaseCoreManager.DatabaseCore.GetAllSensorHistory(sensor.ProductName, sensor.Path));
        //}

        //[Fact]
        //[Trait("Category", "Remove sensor(s) data")]
        //public void RemoveSensorsDataTest()
        //{
        //    int clearedSensorsCount = 0;
        //    void UpdateSensorEventHandler(SensorModel clearedSensor, TransactionType type)
        //    {
        //        Assert.NotNull(clearedSensor);
        //        Assert.Equal(TransactionType.Update, type);

        //        clearedSensorsCount++;
        //    }


        //    var product = GetProductByName("product0");
        //    var sensors = GetAllSensorIdsInBranch(product);

        //    _valuesCache.ChangeSensorEvent += UpdateSensorEventHandler;

        //    _valuesCache.RemoveSensorsData(product.Id);

        //    _valuesCache.ChangeSensorEvent -= UpdateSensorEventHandler;

        //    Assert.Equal(4, clearedSensorsCount);
        //    foreach (var sensorId in sensors)
        //        TestClearedSensor(sensorId);
        //}

        //[Fact]
        //[Trait("Category", "Remove sensor(s) data")]
        //public void RemoveSensorDataTest()
        //{
        //    var sensor = GetSensorByNameFromCache("sensor0");

        //    _valuesCache.RemoveSensorData(sensor.Id);

        //    TestClearedSensor(sensor.Id);
        //    ModelsTester.TestSensorDataWithoutClearedData(sensor, GetSensorByIdFromCache(sensor.Id));
        //}

        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "Add new sensor value")]
        //public void AddNewSensorValueTest(SensorType type)
        //{
        //    int addedProductsCount = 0;
        //    int updatedProductsCount = 0;
        //    void ChangeProductEventHandler(ProductModel product, TransactionType type)
        //    {
        //        Assert.NotNull(product);

        //        if (type == TransactionType.Add)
        //            addedProductsCount++;
        //        else if (type == TransactionType.Update)
        //            updatedProductsCount++;
        //    }

        //    int addedSensorsCount = 0;
        //    int updatedSensorsCount = 0;
        //    void ChangeSensorEventHandler(SensorModel sensor, TransactionType type)
        //    {
        //        Assert.NotNull(sensor);

        //        if (type == TransactionType.Add)
        //            addedSensorsCount++;
        //        else if (type == TransactionType.Update)
        //            updatedSensorsCount++;
        //    }


        //    const string subProductName = "new_subProduct";
        //    const string subSubProductName = "new_subSubProduct";
        //    const string sensorName = "new_sensor";

        //    var timeCollected = DateTime.UtcNow;
        //    var sensorValue = BuildSensorValue(type, $"{subProductName}/{subSubProductName}/{sensorName}");

        //    _valuesCache.ChangeProductEvent += ChangeProductEventHandler;
        //    _valuesCache.ChangeSensorEvent += ChangeSensorEventHandler;

        //    _valuesCache.AddNewSensorValue(sensorValue, timeCollected, new ValidationResult(sensorValue));

        //    _valuesCache.ChangeProductEvent -= ChangeProductEventHandler;
        //    _valuesCache.ChangeSensorEvent -= ChangeSensorEventHandler;

        //    var product = _valuesCache.GetProduct(CommonConstants.SelfMonitoringProductKey);
        //    var subProduct = GetProductByName(subProductName);
        //    var subSubProduct = GetProductByName(subSubProductName);
        //    var sensor = GetSensorByNameFromCache(sensorName);
        //    var sensorDataFromDb = _databaseCoreManager.DatabaseCore.GetLatestSensorValue(product.DisplayName, sensorValue.Path);

        //    Assert.Equal(2, addedProductsCount);
        //    Assert.Equal(5, updatedProductsCount);
        //    Assert.NotEmpty(product.SubProducts);

        //    ModelsTester.TestProductModel(subProductName, subProduct, parentProduct: product, subProducts: new List<ProductModel>() { subSubProduct });
        //    ModelsTester.TestProductModel(subSubProductName, subSubProduct, parentProduct: subProduct, sensors: new List<SensorModel>() { sensor });
        //    ModelsTester.TestProductModel(_databaseCoreManager.DatabaseCore.GetProduct(product.Id), product);
        //    ModelsTester.TestProductModel(_databaseCoreManager.DatabaseCore.GetProduct(subProduct.Id), subProduct);
        //    ModelsTester.TestProductModel(_databaseCoreManager.DatabaseCore.GetProduct(subSubProduct.Id), subSubProduct);

        //    Assert.Equal(1, addedSensorsCount);
        //    Assert.Equal(1, updatedSensorsCount);

        //    ModelsTester.TestSensorModel(sensorValue, product.DisplayName, timeCollected, sensor, parentProduct: subSubProduct);
        //    ModelsTester.TestSensorModel(GetSensorByIdFromDb(sensor.Id), sensor);
        //    ModelsTester.TestSensorModel(sensorDataFromDb, sensor);
        //    SensorValuesTester.TestSensorDataEntity(sensorValue, sensorDataFromDb, timeCollected);
        //}

        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "Add new sensor value")]
        //public void AddNewSensorValue_UpdateData_Test(SensorType type)
        //{
        //    int updatedSensorsCount = 0;
        //    void ChangeSensorEventHandler(SensorModel sensor, TransactionType type)
        //    {
        //        Assert.NotNull(sensor);
        //        Assert.Equal(TransactionType.Update, type);

        //        updatedSensorsCount++;
        //    }


        //    const string sensorName = "new_sensor";

        //    var timeCollected = DateTime.UtcNow;

        //    var sensorValue = BuildSensorValue(type, $"{sensorName}");
        //    _valuesCache.AddNewSensorValue(sensorValue, timeCollected, new ValidationResult(sensorValue));

        //    _valuesCache.ChangeSensorEvent += ChangeSensorEventHandler;

        //    sensorValue = BuildSensorValue(type, $"{sensorName}");
        //    _valuesCache.AddNewSensorValue(sensorValue, timeCollected, new ValidationResult(sensorValue));

        //    _valuesCache.ChangeSensorEvent -= ChangeSensorEventHandler;

        //    var product = _valuesCache.GetProduct(CommonConstants.SelfMonitoringProductKey);
        //    var sensor = GetSensorByNameFromCache(sensorName);
        //    var sensorDataFromDb = _databaseCoreManager.DatabaseCore.GetLatestSensorValue(product.DisplayName, sensorValue.Path);

        //    Assert.Equal(1, updatedSensorsCount);
        //    Assert.NotEmpty(product.Sensors);

        //    ModelsTester.TestSensorModel(sensorValue, product.DisplayName, timeCollected, sensor, parentProduct: product);
        //    ModelsTester.TestSensorModel(GetSensorByIdFromDb(sensor.Id), sensor);
        //    ModelsTester.TestSensorModel(sensorDataFromDb, sensor);
        //    SensorValuesTester.TestSensorDataEntity(sensorValue, sensorDataFromDb, timeCollected);
        //}


        internal static SensorUpdate BuildSensorUpdate(Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                Description = RandomGenerator.GetRandomString(),
                ExpectedUpdateInterval = TimeSpan.FromMinutes(10).ToString(),
                Unit = RandomGenerator.GetRandomString(),
            };


        private async Task TestProductsWithoutParent(List<ProductModel> actualProducts)
        {
            await Task.Delay(100);

            var expectedProducts = _databaseCoreManager.DatabaseCore.GetAllProducts().Where(p => p.ParentProductId == null).ToList();

            ModelsTester.TestProducts(expectedProducts, actualProducts);
        }

        private ProductModel GetProductByName(string name) =>
            _valuesCache.GetTree().FirstOrDefault(p => p.DisplayName == name);

        //private SensorModel GetSensorByNameFromCache(string name) =>
        //    _valuesCache.GetSensors().FirstOrDefault(s => s.SensorName == name);

        //private SensorModel GetSensorByIdFromCache(Guid id) =>
        //    _valuesCache.GetSensors().FirstOrDefault(s => s.Id == id);

        private SensorEntity GetSensorByIdFromDb(Guid id) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().FirstOrDefault(s => s.Id == id.ToString());

        private SensorValueBase BuildSensorValue(SensorType type, string path)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);
            sensorValue.Path = path;
            sensorValue.Description = RandomGenerator.GetRandomString();

            return sensorValue;
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

        //private void TestSensoUpdates(SensorModel sensor, SensorUpdate sensorUpdate)
        //{
        //    var actualSensorFromCache = GetSensorByIdFromCache(sensor.Id);
        //    var actualSensorFromDb = GetSensorByIdFromDb(sensor.Id);

        //    ModelsTester.TestSensorModel(sensorUpdate, actualSensorFromCache);
        //    ModelsTester.TestSensorModel(sensorUpdate, actualSensorFromDb);
        //    ModelsTester.TestSensorModelWithoutUpdatedMetadata(actualSensorFromCache, sensor);
        //    ModelsTester.TestSensorModelWithoutUpdatedMetadata(actualSensorFromDb, sensor);
        //}

        //private void TestClearedSensor(Guid clearedSensorId)
        //{
        //    var sensor = GetSensorByIdFromCache(clearedSensorId);

        //    Assert.NotNull(sensor);
        //    Assert.Equal(DateTime.MinValue, sensor.SensorTime);
        //    Assert.Equal(DateTime.MinValue, sensor.LastUpdateTime);
        //    Assert.Equal(SensorStatus.Unknown, sensor.Status);
        //    Assert.Equal(default, sensor.OriginalFileSensorContentSize);
        //    Assert.Equal(default, sensor.TypedData);

        //    Assert.Empty(_databaseCoreManager.DatabaseCore.GetAllSensorHistory(sensor.ProductName, sensor.Path));
        //    Assert.NotNull(GetSensorByIdFromDb(clearedSensorId));
        //}

        private static void TestRemovedProduct(string productId, GetProductNameById getProductName,
            GetProduct getProduct, GetProductFromDb getProductFromDb)
        {
            Assert.Null(getProductName?.Invoke(productId));
            Assert.Null(getProduct?.Invoke(productId));
            Assert.Null(getProductFromDb?.Invoke(productId));
        }


        private static List<string> GetAllProductIdsInBranch(ProductModel model)
        {
            var products = new List<string>(1 << 2);

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

        private static SensorModel GetClonedSensorModel(SensorModel sensor)
        {
            var sensorEntity = sensor.ToSensorEntity();
            var sensorDataEntity = sensor.ToSensorDataEntity();

            var clonedSensor = new SensorModel(sensorEntity, sensorDataEntity);
            clonedSensor.AddParent(sensor.ParentProduct);

            return clonedSensor;
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
                    var subProduct = EntitiesFactory.BuildProductEntity($"subProduct{j}_{product.DisplayName}", product.Id);

                    var sensor = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = product.Id,
                        ProductName = product.DisplayName,
                        Path = $"sensor{j}",
                        DisplayName = $"sensor{j}",
                        Type = (int)SensorType.BooleanSensor,
                    };
                    var sensorData = new SensorDataEntity()
                    {
                        Path = sensor.Path,
                        TimeCollected = DateTime.UtcNow,
                        DataType = (byte)sensor.Type,
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
                        ProductName = product.DisplayName,
                        Path = $"{subProduct.DisplayName}/{subSubProduct.DisplayName}/sensor",
                        DisplayName = "sensor",
                        Type = (int)SensorType.IntSensor,
                    };
                    var sensorForSubSubProductData = new SensorDataEntity()
                    {
                        Path = sensorForSubSubProduct.Path,
                        TimeCollected = DateTime.UtcNow,
                        DataType = (byte)sensorForSubSubProduct.Type,
                        TypedData = JsonSerializer.Serialize(new IntSensorData() { IntValue = 12345, Comment = "sensorData1" }),
                        Status = (byte)SensorStatus.Ok,
                    };

                    subSubProduct.AddSensor(sensorForSubSubProduct.Id);

                    products.Add(subProduct);
                    products.Add(subSubProduct);

                    sensors.Add(sensor);
                    sensors.Add(sensorForSubSubProduct);

                    sensorsData.Add((product.DisplayName, sensorData));
                    sensorsData.Add((product.DisplayName, sensorForSubSubProductData));
                }

                products.Add(product);
            }

            return (products, sensors, sensorsData);
        }
    }
}
