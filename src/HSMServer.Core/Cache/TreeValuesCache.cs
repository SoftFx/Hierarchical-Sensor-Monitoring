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
        private readonly IDatabaseCore _databaseCore;
        private readonly IUserManager _userManager;

        private readonly ConcurrentDictionary<string, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, SensorModel> _sensors;
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys;

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<SensorModel, TransactionType> ChangeSensorEvent;
        public event Action<SensorModel> UploadSensorDataEvent;


        public TreeValuesCache(IDatabaseCore databaseCore, IUserManager userManager)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;

            _tree = new ConcurrentDictionary<string, ProductModel>();
            _sensors = new ConcurrentDictionary<Guid, SensorModel>();
            _keys = new ConcurrentDictionary<Guid, AccessKeyModel>();

            Initialize();
        }


        public List<ProductModel> GetTree() => _tree.Values.ToList();

        public List<SensorModel> GetSensors() => _sensors.Values.ToList();

        public List<AccessKeyModel> GetAccessKeys() => _keys.Values.ToList();

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
                _databaseCore.RemoveProductNew(product.Id);

                _userManager.RemoveProductFromUsers(product.Id);

                ChangeProductEvent?.Invoke(product, TransactionType.Delete);
            }

            if (_tree.TryGetValue(productId, out var product))
            {
                RemoveProduct(productId);

                if (product.ParentProduct != null)
                    UpdateProduct(product.ParentProduct);
            }
        }

        public ProductModel GetProduct(string id)
        {
            _tree.TryGetValue(id, out var product);
            return product;
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

        public void AddAccessKey(AccessKeyModel key)
        {
            if (_keys.ContainsKey(key.Id)) 
                return;

            _keys.TryAdd(key.Id, key);
            AddAccessKeyToProduct(key);

            _databaseCore.AddAccessKey(key.ToAccessKeyEntity());
        }

        public void RemoveAccessKey(Guid id)
        {
            if (!_keys.TryGetValue(id, out var key))
                return;

            if (key.ProductId != null && _tree.TryGetValue(key.ProductId, out var product))
            {
                product.AccessKeys.TryRemove(id, out _);
                ChangeProductEvent?.Invoke(product, TransactionType.Update);
            }

            _databaseCore.RemoveAccessKey(id.ToString());
        }

        public AccessKeyModel GetAccessKey(Guid id)
        {
            _keys.TryGetValue(id, out var key);
            return key;
        }

        public void UpdateAccessKey(AccessKeyModel updatedKey)
        {
            if (!_keys.TryGetValue(updatedKey.Id, out var key))
                return;

            _keys[updatedKey.Id] = updatedKey;
            _databaseCore.UpdateAccessKey(updatedKey.ToAccessKeyEntity());
        }

        public void UpdateSensor(SensorUpdate updatedSensor)
        {
            if (!_sensors.TryGetValue(updatedSensor.Id, out var sensor))
                return;

            sensor.Update(updatedSensor);
            _databaseCore.UpdateSensor(sensor.ToSensorEntity());

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }

        public void RemoveSensor(Guid sensorId)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            sensor.ParentProduct.Sensors.TryRemove(sensorId, out _);
            _databaseCore.RemoveSensorWithMetadata(sensor.ProductName, sensor.Path);

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
            _databaseCore.RemoveSensor(sensor.ProductName, sensor.Path);

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
                UpdateProduct(parentProduct);
            }
            else
            {
                bool isMetadataUpdated = sensor.IsSensorMetadataUpdated(sensorValue);

                sensor.UpdateData(sensorValue, timeCollected, validationResult);

                if (isMetadataUpdated)
                    _databaseCore.UpdateSensor(sensor.ToSensorEntity());
            }

            _databaseCore.PutSensorData(sensor.ToSensorDataEntity(), productName);

            UploadSensorDataEvent?.Invoke(sensor);
        }

        private void Initialize()
        {
            var productEntities = _databaseCore.GetAllProducts();
            var sensorEntities = _databaseCore.GetAllSensors();
            var accessKeysEntities = _databaseCore.GetAccessKeys();

            BuildTree(productEntities.Where(e => !e.IsConverted).ToList(),
                      sensorEntities.Where(e => !e.IsConverted).ToList());

            BuildTreeWithMigration(
                productEntities.Where(e => e.IsConverted).ToList(),
                sensorEntities.Where(e => e.IsConverted).ToList());

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (productEntities.Count == 0 || monitoringProduct == null)
                AddSelfMonitoringProduct();

            BuildKeys(accessKeysEntities.ToList());

            GenerateDefaultAccessKeys();
        }

        private void BuildTree(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            var productsToResave = FillTreeByProductModels(productEntities);

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

            ResaveProducts(productsToResave);
        }

        private SensorDataEntity GetSensorData(SensorEntity sensor) =>
            _databaseCore.GetLatestSensorValue(sensor.ProductName, sensor.Path);

        private void BuildTreeWithMigration(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            FillTreeByProductModels(productEntities);
            FillTreeBySensorModels(sensorEntities);

            ResaveEntities(productEntities, sensorEntities);
        }

        private void BuildKeys(List<AccessKeyEntity> entities) 
        {
            foreach(var keyEntity in entities)
            {
                var key = new AccessKeyModel(keyEntity);
                _keys.TryAdd(key.Id, key);

                AddAccessKeyToProduct(key);
            }
        }

        private void GenerateDefaultAccessKeys()
        {
            foreach(var product in _tree.Values)
            {
                if (product.AccessKeys.Count > 0) 
                    continue;

                AddAccessKey(AccessKeyModel.BuildDefault(product));
            }
        }

        private List<string> FillTreeByProductModels(List<ProductEntity> productEntities)        
        {
            var productsToResave = new List<string>();

            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);

                if (!productEntity.IsConverted && productEntity.ParentProductId?.Length == 0)
                    productsToResave.Add(productEntity.Id);
            }

            return productsToResave;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillTreeBySensorModels(List<SensorEntity> sensorEntities)
        {
            foreach (var sensorEntity in sensorEntities)
            {
                if (sensorEntity.Path == null)
                    continue;

                var parentProduct = AddNonExistingProductsAndGetParentProduct(sensorEntity.ProductName, sensorEntity.Path);

                var sensor = new SensorModel(sensorEntity, GetSensorData(sensorEntity));
                parentProduct.AddSensor(sensor);

                _sensors.TryAdd(sensor.Id, sensor);

                _databaseCore.UpdateProduct(parentProduct.ToProductEntity());
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
                    UpdateProduct(parentProduct);
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
            var product = new ProductModel(CommonConstants.SelfMonitoringProductKey, CommonConstants.SelfMonitoringProductName);

            AddProduct(product);
        }

        private void AddProduct(ProductModel product)
        {
            _tree.TryAdd(product.Id, product);
            _databaseCore.AddProductNew(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Add);
        }

        private void AddSensor(SensorModel sensor)
        {
            _sensors.TryAdd(sensor.Id, sensor);
            _databaseCore.AddSensor(sensor.ToSensorEntity());

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Add);
        }

        private void UpdateProduct(ProductModel product)
        {
            _databaseCore.UpdateProduct(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Update);
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

                _databaseCore.UpdateProduct(product.ToProductEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveProducts(List<string> productIds)
        {
            foreach (var productId in productIds)
            {
                if (!_tree.TryGetValue(productId, out var product))
                    continue;

                _databaseCore.UpdateProduct(product.ToProductEntity());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResaveSensors(List<SensorEntity> sensorEntities)
        {
            foreach (var sensorEntity in sensorEntities)
            {
                if (sensorEntity.Path == null)
                    _databaseCore.RemoveSensorWithMetadata(sensorEntity.ProductName, sensorEntity.Path);

                if (!sensorEntity.IsConverted || !_sensors.TryGetValue(Guid.Parse(sensorEntity.Id), out var sensor))
                    continue;

                _databaseCore.UpdateSensor(sensor.ToSensorEntity());
            }
        }

        private void AddAccessKeyToProduct(AccessKeyModel key)
        {
            if (key.ProductId != null && _tree.TryGetValue(key.ProductId, out var product))
            {
                product.AddAccessKey(key);
                ChangeProductEvent?.Invoke(product, TransactionType.Update);
            }
        }
    }
}
