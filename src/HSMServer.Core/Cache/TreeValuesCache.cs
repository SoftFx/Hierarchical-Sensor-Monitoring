using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataValidation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache
    {
        private readonly IDatabaseAdapter _database;
        private readonly IProductManager _productManager;

        private readonly ConcurrentDictionary<string, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, SensorModel> _sensors;

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<SensorModel, TransactionType> ChangeSensorEvent;
        public event Action<SensorModel> UploadSensorDataEvent;


        public TreeValuesCache(IDatabaseAdapter database, IProductManager productManager)
        {
            _database = database;
            _productManager = productManager;

            _tree = new ConcurrentDictionary<string, ProductModel>();
            _sensors = new ConcurrentDictionary<Guid, SensorModel>();

            (var products, var sensors, var sensorsData) = GenerateTestData();//GenerateTestDataNeedToResave();

            BuildTree(products, sensors, sensorsData);
            //Initialize(products, sensors, sensorsData);
        }


        public List<ProductModel> GetTree() => _tree.Values.ToList();

        public List<SensorModel> GetSensors() => _sensors.Values.ToList();

        public void AddProduct(string productName)
        {
            var product = new ProductModel(productName);

            _productManager.AddProduct(product);
            //_database.AddProduct(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Add);
        }

        public void AddNewSensorValue(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult)
        {
            var parentProductName = _productManager.GetProductNameByKey(sensorValue.Key);  // TODO? get product by key from cache (_tree)?
            var parentProduct = AddNonExistingProductsAndGetParentProduct(parentProductName, sensorValue.Path);

            var newSensorValueName = sensorValue.Path.Split(CommonConstants.SensorPathSeparator)[^1];
            var sensor = parentProduct.Sensors.FirstOrDefault(s => s.Value.SensorName == newSensorValueName).Value;
            if (sensor == null)
            {
                sensor = new SensorModel(sensorValue, timeCollected, validationResult);
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);
                ChangeSensorEvent?.Invoke(sensor, TransactionType.Add);
            }
            else
                sensor.UpdateData(sensorValue, timeCollected, validationResult);

            //_database.PutSensorData(sensor.ToSensorDataEntity(), parentProductName); // TODO: save to db

            UploadSensorDataEvent?.Invoke(sensor);
        }

        // TODO  -> method is right after adding new property NeedToResave to product and sensor entities
        private void Initialize(List<ProductEntity> productEntities,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            //BuildTree(productEntities.Where(e => !e.NeedToResave).ToList(),
            //          sensorEntities.Where(e => !e.NeedToResave).ToList(),
            //          sensorDataEntities);

            //BuildTreeWithMigration(
            //    productEntities.Where(e => e.NeedToResave).ToList(),
            //    sensorEntities.Where(e => e.NeedToResave).ToList(),
            //    sensorDataEntities);
        }

        private void BuildTree(List<ProductEntity> productEntities,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            FillTreeByProductModels(productEntities);

            var sensorDatas = sensorDataEntities.Where(d => !string.IsNullOrEmpty(d.Id)).ToDictionary(s => s.Id); // TODO: dictionary (key, list<values>) for several datas
            foreach (var sensorEntity in sensorEntities)
            {
                var sensor = new SensorModel(sensorEntity, sensorDatas[sensorEntity.Id]);
                _sensors.TryAdd(sensor.Id, sensor);
            }

            foreach (var productEntity in productEntities)
                if (_tree.TryGetValue(productEntity.Id, out var product))
                {
                    if (productEntity.SubProductsIds != null)
                        foreach (var subProductId in productEntity.SubProductsIds)
                        {
                            if (_tree.TryGetValue(subProductId, out var subProduct))
                                product.AddSubProduct(subProduct);
                        }

                    if (productEntity.SensorsIds != null)
                        foreach (var sensorId in productEntity.SensorsIds)
                        {
                            if (_sensors.TryGetValue(Guid.Parse(sensorId), out var sensor))
                                product.AddSensor(sensor);
                        }
                }
        }

        private void BuildTreeWithMigration(List<ProductEntity> productEntities,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            var newProductIds = new List<string>();
            void AddNewProductHandler(ProductModel product, TransactionType transaction)
            {
                if (transaction == TransactionType.Add)
                    newProductIds.Add(product.Id);
            }

            ChangeProductEvent += AddNewProductHandler;

            FillTreeByProductModels(productEntities);
            FillTreeBySensorModels(sensorEntities, sensorDataEntities);

            ChangeProductEvent -= AddNewProductHandler;

            //ResaveEntities(productEntities, newProductIds, sensorEntities, sensorDataEntities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillTreeBySensorModels(List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            var sensorDatas = sensorDataEntities.Where(d => !string.IsNullOrEmpty(d.Path)).ToDictionary(s => s.Path); // TODO: dictionary (key, list<values>) for several datas
            foreach (var sensorEntity in sensorEntities)
            {
                var parentProduct = AddNonExistingProductsAndGetParentProduct(sensorEntity.ProductName, sensorEntity.Path);

                var sensor = new SensorModel(sensorEntity, sensorDatas[sensorEntity.Path]);
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveEntities(List<ProductEntity> productEntities, List<string> newProductIds,
            List<SensorEntity> sensorEntities, List<SensorDataEntity> sensorDataEntities)
        {
            ResaveProducts(productEntities);
            SaveNewProducts(newProductIds);
            ResaveSensors(sensorEntities);
            ResaveSensorDatas(sensorDataEntities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveProducts(List<ProductEntity> productEntities)
        {
            foreach (var productEntity in productEntities)
            {
                if (/*!productEntity.NeedToResave || */!_tree.TryGetValue(productEntity.Id, out var product))
                    continue;

                _database.UpdateProduct(product.ToProductEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SaveNewProducts(List<string> productsIdsToSave)
        {
            foreach (var productId in productsIdsToSave)
            {
                if (_tree.TryGetValue(productId, out var product))
                    _database.UpdateProduct(product.ToProductEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveSensors(List<SensorEntity> sensorEntities)
        {
            foreach (var sensorEntity in sensorEntities)
            {
                if (/*!sensorEntity.NeedToResave || */!_sensors.TryGetValue(Guid.Parse(sensorEntity.Id), out var sensor))
                    continue;

                _database.UpdateSensor(sensor.ToSensorEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveSensorDatas(List<SensorDataEntity> sensorDataEntities)
        {
            // TODO
        }


        private void FillTreeByProductModels(List<ProductEntity> productEntities)
        {
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }
        }

        private ProductModel AddNonExistingProductsAndGetParentProduct(string productName, string sensorPath)
        {
            var parentProduct = _tree.FirstOrDefault(p => p.Value.DisplayName == productName).Value;
            if (parentProduct == null)
            {
                parentProduct = new ProductModel(productName);

                _tree.TryAdd(parentProduct.Id, parentProduct);
                ChangeProductEvent?.Invoke(parentProduct, TransactionType.Add);
            }

            var pathParts = sensorPath.Split(CommonConstants.SensorPathSeparator);
            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                var subProductName = pathParts[i];
                var subProduct = parentProduct.SubProducts.FirstOrDefault(p => p.Value.DisplayName == subProductName).Value;
                if (subProduct == null)
                {
                    subProduct = new ProductModel(subProductName, parentProduct);
                    parentProduct.AddSubProduct(subProduct);

                    _tree.TryAdd(subProduct.Id, subProduct);
                    ChangeProductEvent?.Invoke(subProduct, TransactionType.Add);
                }

                parentProduct = subProduct;
            }

            return parentProduct;
        }

        private static (List<ProductEntity>, List<SensorEntity>, List<SensorDataEntity>) GenerateTestData()
        {
            var products = new List<ProductEntity>();
            var sensors = new List<SensorEntity>();
            var sensorsData = new List<SensorDataEntity>();

            for (int i = 0; i < 2; ++i)
            {
                var product = new ProductEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    State = (int)ProductState.FullAccess,
                    DisplayName = $"product{i}",
                    CreationDate = DateTime.UtcNow.Ticks,
                    SubProductsIds = new List<string>(),
                    SensorsIds = new List<string>(),
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
                        SubProductsIds = new List<string>(),
                    };

                    var sensor = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = product.Id,
                        SensorName = $"sensor{j}",
                        SensorType = (int)SensorType.BooleanSensor,
                    };
                    var sensorData = new SensorDataEntity()
                    {
                        Id = sensor.Id,
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
                        SensorsIds = new List<string>(),
                    };

                    subProduct.SubProductsIds.Add(subSubProduct.Id);

                    var sensorForSubSubProduct = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = subSubProduct.Id,
                        SensorName = "sensor",
                        SensorType = (int)SensorType.IntSensor,
                    };
                    var sensorForSubSubProductData = new SensorDataEntity()
                    {
                        Id = sensorForSubSubProduct.Id,
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

                    sensorsData.Add(sensorData);
                    sensorsData.Add(sensorForSubSubProductData);
                }

                products.Add(product);
            }

            return (products, sensors, sensorsData);
        }

        private static (List<ProductEntity>, List<SensorEntity>, List<SensorDataEntity>) GenerateTestDataNeedToResave()
        {
            var products = new List<ProductEntity>();
            var sensors = new List<SensorEntity>();
            var sensorsData = new List<SensorDataEntity>();

            for (int i = 0; i < 2; ++i)
            {
                string productName = $"product{i}";

                var sensor1 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"sensor{i}",
                    SensorType = 0,
                };
                var sensor11 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"sensor{i}1",
                    SensorType = 7,
                };
                var sensor2 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"subProduct/sensor{i}",
                    SensorType = 0,
                };
                var sensor3 = new SensorEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductName = productName,
                    Path = $"subProduct/subSubProduct/sensor{i}",
                    SensorType = 0,
                };
                sensors.Add(sensor1);
                sensors.Add(sensor11);
                sensors.Add(sensor2);
                sensors.Add(sensor3);

                //foreach (var s in sensors)
                //    s.NeedToResave = true;

                var sensorDaata1 = new SensorDataEntity()
                {
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor1.Path,
                    DataType = (byte)sensor1.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = true, Comment = "sensorData1" }),
                    Status = (byte)SensorStatus.Warning,
                };
                var sensorDaata11 = new SensorDataEntity()
                {
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor11.Path,
                    DataType = (byte)sensor11.SensorType,
                    TypedData = JsonSerializer.Serialize(new FileSensorBytesData() { FileContent = Encoding.UTF8.GetBytes("123"), FileName = "filename", Extension = "txt", Comment = "sensorData11" }),
                    Status = (byte)SensorStatus.Ok,
                };
                var sensorDaata2 = new SensorDataEntity()
                {
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor2.Path,
                    DataType = (byte)sensor2.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = false, Comment = "sensorData2" }),
                    Status = (byte)SensorStatus.Ok,
                };
                var sensorDaata3 = new SensorDataEntity()
                {
                    TimeCollected = DateTime.UtcNow,
                    Path = sensor3.Path,
                    DataType = (byte)sensor3.SensorType,
                    TypedData = JsonSerializer.Serialize(new BoolSensorData() { BoolValue = false, Comment = "sensorData3" }),
                    Status = (byte)SensorStatus.Ok,
                };
                sensorsData.Add(sensorDaata1);
                sensorsData.Add(sensorDaata11);
                sensorsData.Add(sensorDaata2);
                sensorsData.Add(sensorDaata3);

                products.Add(new ProductEntity()
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = productName,
                    //NeedToResave = true,
                });
            }

            return (products, sensors, sensorsData);
        }
    }
}
