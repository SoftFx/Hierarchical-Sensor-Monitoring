﻿using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsDataValidation;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache
    {
        private const string ErrorPathKey = "Path or key is empty.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IDatabaseCore _databaseCore;
        private readonly IUserManager _userManager;

        private readonly ConcurrentDictionary<string, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, SensorModel> _sensors;
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys;

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<SensorModel, TransactionType> ChangeSensorEvent;
        public event Action<AccessKeyModel, TransactionType> ChangeAccessKeyEvent;


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
                _databaseCore.RemoveProduct(product.Id);

                foreach (var (id, _) in product.AccessKeys)
                    RemoveAccessKey(id);

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

        public ProductModel GetProduct(string id) => _tree.GetValueOrDefault(id);

        public string GetProductNameById(string id) => GetProduct(id)?.DisplayName;

        public bool TryGetProductByKey(string key, out ProductModel product, out string message)
        {
            key = GetAccessKeyModel(key)?.ProductId ?? key;

            var hasProduct = _tree.TryGetValue(key, out product);
            message = hasProduct ? string.Empty : ErrorKeyNotFound;

            return hasProduct;
        }

        private AccessKeyModel GetAccessKeyModel(string key) =>
            Guid.TryParse(key, out var guid) ? _keys.GetValueOrDefault(guid) : null;

        public List<ProductModel> GetProducts(User user, bool withoutParent = true)
        {
            var products = _tree.Values.ToList();
            if (withoutParent)
                products = products.Where(p => p.ParentProduct == null).ToList();

            if (user == null || user.IsAdmin)
                return products;

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return null;

            var availableProducts = products.Where(p => ProductRoleHelper.IsAvailable(p.Id, user.ProductsRoles)).ToList();

            return withoutParent ? availableProducts : GetAllProductsWithTheirSubProducts(availableProducts);
        }

        public bool TryCheckKeyPermissions(string key, string path, out string message)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(path))
            {
                message = ErrorPathKey;
                return false;
            }

            if (!TryGetProductByKey(key, out var product, out message))
                return false;
            else if (product.Id == key)
                return true;

            var accessKey = GetAccessKeyModel(key);
            if (!accessKey.HasPermissionForSendData(out message))
                return false;

            if (accessKey.Permissions.HasFlag(KeyPermissions.CanAddProducts | KeyPermissions.CanAddSensors))
                return true;

            return IsValidKeyForPath(path, product, accessKey, out message);
        }


        public void AddAccessKey(AccessKeyModel key)
        {
            if (AddKeyToTree(key))
            {
                _databaseCore.AddAccessKey(key.ToAccessKeyEntity());

                ChangeAccessKeyEvent?.Invoke(key, TransactionType.Add);
            }
        }

        public void RemoveAccessKey(Guid id)
        {
            if (!_keys.TryRemove(id, out var key))
                return;

            if (key.ProductId != null && _tree.TryGetValue(key.ProductId, out var product))
            {
                product.AccessKeys.TryRemove(id, out _);
                ChangeProductEvent?.Invoke(product, TransactionType.Update);
            }

            _databaseCore.RemoveAccessKey(id);

            ChangeAccessKeyEvent?.Invoke(key, TransactionType.Delete);
        }

        public void UpdateAccessKey(AccessKeyUpdate updatedKey)
        {
            if (!_keys.TryGetValue(updatedKey.Id, out var key))
                return;

            key.Update(updatedKey);
            _databaseCore.UpdateAccessKey(key.ToAccessKeyEntity());

            ChangeAccessKeyEvent?.Invoke(key, TransactionType.Update);
        }

        public AccessKeyModel GetAccessKey(Guid id)
        {
            _keys.TryGetValue(id, out var key);
            return key;
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

        public void AddNewSensorValue(SensorValueBase sensorValue, DateTime timeCollected,
            ValidationResult validationResult, bool saveDataToDb = true)
        {
            if (!TryGetProductByKey(sensorValue.Key, out var product, out _))
                return;

            var productName = product.DisplayName;
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

            if (saveDataToDb)
                _databaseCore.PutSensorData(sensor.ToSensorDataEntity(), productName);

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }

        private void Initialize()
        {
            _logger.Info($"{nameof(TreeValuesCache)} is initializing");

            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} is requesting");
            var productEntities = _databaseCore.GetAllProducts();
            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} requested");

            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} is requesting");
            var sensorEntities = _databaseCore.GetAllSensors();
            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} requested");

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _databaseCore.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            BuildTree(productEntities, sensorEntities);

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (productEntities.Count == 0 || monitoringProduct == null)
                AddSelfMonitoringProduct();

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys(accessKeysEntities.ToList());
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            _logger.Info($"{nameof(TreeValuesCache)} initialized");
        }

        private void BuildTree(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities)
        {
            _logger.Info($"{nameof(productEntities)} are applying");
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }
            _logger.Info($"{nameof(productEntities)} applied");

            _logger.Info($"{nameof(sensorEntities)} are applying");
            foreach (var sensorEntity in sensorEntities)
            {
                var sensor = new SensorModel(sensorEntity, GetSensorData(sensorEntity));
                _sensors.TryAdd(sensor.Id, sensor);
            }
            _logger.Info($"{nameof(sensorEntities)} applied");

            _logger.Info("Tree is building");
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
            _logger.Info("Tree built");
        }

        private SensorDataEntity GetSensorData(SensorEntity sensor) =>
            _databaseCore.GetLatestSensorValue(sensor.ProductName, sensor.Path);

        private void ApplyAccessKeys(List<AccessKeyEntity> entities)
        {
            foreach (var keyEntity in entities)
            {
                AddKeyToTree(new AccessKeyModel(keyEntity));
            }

            foreach (var product in _tree.Values)
            {
                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
            }
        }

        private ProductModel AddNonExistingProductsAndGetParentProduct(string productName, string sensorPath)
        {
            var parentProduct = _tree.FirstOrDefault(p => p.Value.DisplayName == productName).Value;
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

        private void AddSelfMonitoringProduct()
        {
            var product = new ProductModel(CommonConstants.SelfMonitoringProductKey, CommonConstants.SelfMonitoringProductName);

            AddProduct(product);
        }

        private void AddProduct(ProductModel product)
        {
            _tree.TryAdd(product.Id, product);
            _databaseCore.AddProduct(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Add);

            foreach (var (_, key) in product.AccessKeys)
                AddAccessKey(key);

            if (product.AccessKeys.IsEmpty)
                AddAccessKey(AccessKeyModel.BuildDefault(product));
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

        private bool AddKeyToTree(AccessKeyModel key)
        {
            bool isSuccess = _keys.TryAdd(key.Id, key);

            if (isSuccess && key.ProductId != null
                && _tree.TryGetValue(key.ProductId, out var product))
            {
                isSuccess &= product.AddAccessKey(key);
                ChangeProductEvent?.Invoke(product, TransactionType.Update);
            }

            return isSuccess;
        }

        private static bool IsValidKeyForPath(string path, ProductModel product,
            AccessKeyModel accessKey, out string message)
        {
            message = string.Empty;

            var parts = path.Split(CommonConstants.SensorPathSeparator);

            for (int i = 0; i < parts.Length; i++)
            {
                var expectedName = parts[i];

                if (i != parts.Length - 1)
                {
                    product = product.SubProducts.FirstOrDefault(sp => sp.Value.DisplayName
                        .Equals(expectedName)).Value;

                    if (product == null && !accessKey.HasPermissionCreateProductBranch(out message))
                        return false;
                }
                else
                {
                    if (!product.Sensors.Any(s => s.Value.SensorName == expectedName) &&
                        !accessKey.IsHasPermission(KeyPermissions.CanAddSensors, out message))
                        return false;
                }
            }
            return true;
        }

        private List<ProductModel> GetAllProductsWithTheirSubProducts(List<ProductModel> products)
        {
            var productsWithTheirSubProducts = new Dictionary<string, ProductModel>(products.Count);
            foreach (var product in products)
            {
                productsWithTheirSubProducts.Add(product.Id, product);
                GetAllProductSubProducts(product, productsWithTheirSubProducts);
            }

            return productsWithTheirSubProducts.Values.ToList();
        }

        private void GetAllProductSubProducts(ProductModel product, Dictionary<string, ProductModel> allSubProducts)
        {
            foreach (var (subProductId, subProduct) in product.SubProducts)
            {
                if (!allSubProducts.ContainsKey(subProductId))
                    allSubProducts.Add(subProductId, subProduct);

                GetAllProductSubProducts(subProduct, allSubProducts);
            }
        }
    }
}
