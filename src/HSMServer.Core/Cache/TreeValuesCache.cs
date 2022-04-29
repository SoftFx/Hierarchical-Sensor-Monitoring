using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsDataValidation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache
    {
        private readonly IDatabaseAdapter _database;
        private readonly IUserManager _userManager;

        private readonly ConcurrentDictionary<string, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, SensorModel> _sensors;

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<SensorModel, TransactionType> ChangeSensorEvent;
        public event Action<SensorModel> UploadSensorDataEvent;


        public TreeValuesCache(IDatabaseAdapter database, IUserManager userManager)
        {
            _database = database;
            _userManager = userManager;

            _tree = new ConcurrentDictionary<string, ProductModel>();
            _sensors = new ConcurrentDictionary<Guid, SensorModel>();

            var productsFromDb = _database.GetAllProducts();
            var sensorsFromDb = _database.GetAllSensors();

            Initialize(productsFromDb, sensorsFromDb);
        }


        public List<ProductModel> GetTree() => _tree.Values.ToList();

        public List<SensorModel> GetSensors() => _sensors.Values.ToList();

        public ProductModel AddProduct(string productName)
        {
            var product = new ProductModel(productName);

            AddProduct(product);

            return product;
        }

        public void RemoveProduct(string productId)
        {
            void RemoveProduct(string productId)
            {
                if (!_tree.TryRemove(productId, out var product))
                    return;

                foreach (var (subProductId, _) in product.SubProducts)
                    RemoveProduct(subProductId);

                foreach (var (sensorId, _) in product.Sensors)
                    RemoveSensor(sensorId);

                product.ParentProduct?.SubProducts.TryRemove(productId, out _);
                _database.RemoveProductNew(product.Id);

                _userManager.RemoveProductFromUsers(product.Id);

                ChangeProductEvent?.Invoke(product, TransactionType.Delete);
            }

            if (_tree.TryGetValue(productId, out var product))
            {
                RemoveProduct(productId);

                if (product.ParentProduct != null)
                {
                    _database.UpdateProductNew(product.ParentProduct.ToProductEntity());

                    ChangeProductEvent?.Invoke(product.ParentProduct, TransactionType.Update);
                }
            }
        }

        public ProductModel GetProduct(string id)
        {
            if (_tree.TryGetValue(id, out var product))
                return product;

            return null;
        }

        public string GetProductNameById(string id) => GetProduct(id)?.DisplayName;

        public List<ProductModel> GetProductsWithoutParent(User user)
        {
            var products = _tree.Values.Where(p => p.ParentProduct == null).ToList();

            if (user == null || user.IsAdmin)
                return products;

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return null;

            return products.Where(p => ProductRoleHelper.IsAvailable(p.Id, user.ProductsRoles)).ToList();
        }

        public void UpdateSensor(UpdatedSensor updatedSensor)
        {
            if (!_sensors.TryGetValue(updatedSensor.Id, out var sensor))
                return;

            sensor.Update(updatedSensor);
            _database.UpdateSensor(sensor.ToSensorEntity());

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }

        public void RemoveSensor(Guid sensorId)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            sensor.ParentProduct.Sensors.TryRemove(sensorId, out _);
            _database.RemoveSensorWithMetadata(sensor.ProductName, sensor.Path);

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Delete);
        }

        public void RemoveSensorsData(string productId)
        {
            if (!_tree.TryGetValue(productId, out var product))
                return;

            foreach (var (subProductId, _) in product.SubProducts)
                RemoveSensorsData(subProductId);

            foreach (var (sensorId, _) in product.Sensors)
                RemoveSensorData(sensorId);
        }

        public void RemoveSensorData(Guid sensorId)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor))
                return;

            sensor.ClearData();
            _database.RemoveSensor(sensor.ProductName, sensor.Path);

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }

        public void AddNewSensorValue(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult)
        {
            var productName = GetProductNameById(sensorValue.Key);
            if (string.IsNullOrEmpty(productName))
                return;

            var parentProduct = AddNonExistingProductsAndGetParentProduct(productName, sensorValue.Path);

            var newSensorValueName = sensorValue.Path.Split(CommonConstants.SensorPathSeparator)[^1];
            var sensor = parentProduct.Sensors.FirstOrDefault(s => s.Value.SensorName == newSensorValueName).Value;
            if (sensor == null)
            {
                sensor = new SensorModel(sensorValue, productName, timeCollected, validationResult);
                parentProduct.AddSensor(sensor);

                AddSensor(sensor);
                _database.UpdateProductNew(parentProduct.ToProductEntity());
            }
            else
                sensor.UpdateData(sensorValue, timeCollected, validationResult);

            _database.PutSensorData(sensor.ToSensorDataEntity(), productName);

            UploadSensorDataEvent?.Invoke(sensor);
        }

        private void Initialize(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            BuildTree(productEntities.Where(e => !e.IsConverted).ToList(),
                      sensorEntities.Where(e => !e.IsConverted).ToList());

            BuildTreeWithMigration(
                productEntities.Where(e => e.IsConverted).ToList(),
                sensorEntities.Where(e => e.IsConverted).ToList());

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (productEntities.Count == 0 || monitoringProduct == null)
                AddSelfMonitoringProduct();
        }

        private void BuildTree(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            FillTreeByProductModels(productEntities);

            foreach (var sensorEntity in sensorEntities)
            {
                var sensor = new SensorModel(sensorEntity, GetSensorData(sensorEntity));
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

        private SensorDataEntity GetSensorData(SensorEntity sensor) =>
            _database.GetLastSensorValue(sensor.ProductName, sensor.Path);

        private void BuildTreeWithMigration(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            FillTreeByProductModels(productEntities);
            FillTreeBySensorModels(sensorEntities);

            ResaveEntities(productEntities, sensorEntities);
        }

        private void FillTreeByProductModels(List<ProductEntity> productEntities)
        {
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillTreeBySensorModels(List<SensorEntity> sensorEntities)
        {
            foreach (var sensorEntity in sensorEntities)
            {
                var parentProduct = AddNonExistingProductsAndGetParentProduct(sensorEntity.ProductName, sensorEntity.Path);

                var sensor = new SensorModel(sensorEntity, GetSensorData(sensorEntity));
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);

                _database.UpdateProductNew(parentProduct.ToProductEntity());
            }
        }

        private ProductModel AddNonExistingProductsAndGetParentProduct(string productName, string sensorPath)
        {
            var parentProduct = GetProductByName(productName);
            if (parentProduct == null)
                parentProduct = AddProduct(productName);

            var pathParts = sensorPath.Split(CommonConstants.SensorPathSeparator);
            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                var subProductName = pathParts[i];
                var subProduct = parentProduct.SubProducts.FirstOrDefault(p => p.Value.DisplayName == subProductName).Value;
                if (subProduct == null)
                {
                    subProduct = new ProductModel(subProductName);
                    parentProduct.AddSubProduct(subProduct);

                    AddProduct(subProduct);
                    _database.UpdateProductNew(parentProduct.ToProductEntity());
                }

                parentProduct = subProduct;
            }

            return parentProduct;
        }

        /// <returns>"true" product (without parent) with name = name</returns>
        private ProductModel GetProductByName(string name) =>
            _tree.FirstOrDefault(p => p.Value.ParentProduct == null && p.Value.DisplayName == name).Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSelfMonitoringProduct()
        {
            var product = new ProductModel(CommonConstants.SelfMonitoringProductKey);

            AddProduct(product);
        }

        private void AddProduct(ProductModel product)
        {
            _tree.TryAdd(product.Id, product);
            _database.AddProductNew(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Add);
        }

        private void AddSensor(SensorModel sensor)
        {
            _sensors.TryAdd(sensor.Id, sensor);
            _database.AddSensor(sensor.ToSensorEntity());

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Add);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveEntities(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            ResaveProducts(productEntities);
            ResaveSensors(sensorEntities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveProducts(List<ProductEntity> productEntities)
        {
            foreach (var productEntity in productEntities)
            {
                if (!productEntity.IsConverted || !_tree.TryGetValue(productEntity.Id, out var product))
                    continue;

                _database.UpdateProductNew(product.ToProductEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveSensors(List<SensorEntity> sensorEntities)
        {
            foreach (var sensorEntity in sensorEntities)
            {
                if (!sensorEntity.IsConverted || !_sensors.TryGetValue(Guid.Parse(sensorEntity.Id), out var sensor))
                    continue;

                _database.UpdateSensor(sensor.ToSensorEntity());
            }
        }
    }
}
