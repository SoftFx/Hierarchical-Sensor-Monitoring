﻿using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsUpdatesQueue;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache, IDisposable
    {
        private const string ErrorPathKey = "Path or key is empty.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorTooLongPath = "Path for the sensor is too long.";
        private const string NotInitializedCacheError = "Cache is not initialized yet.";

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IDatabaseCore _databaseCore;
        private readonly IUserManager _userManager;
        private readonly IUpdatesQueue _updatesQueue;

        private readonly ConcurrentDictionary<string, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors;
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys;

        public bool IsInitialized { get; private set; }

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<BaseSensorModel, TransactionType> ChangeSensorEvent;
        public event Action<AccessKeyModel, TransactionType> ChangeAccessKeyEvent;


        public TreeValuesCache(IDatabaseCore databaseCore, IUserManager userManager, IUpdatesQueue updatesQueue)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;

            _updatesQueue = updatesQueue;
            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            _tree = new ConcurrentDictionary<string, ProductModel>();
            _sensors = new ConcurrentDictionary<Guid, BaseSensorModel>();
            _keys = new ConcurrentDictionary<Guid, AccessKeyModel>();

            Initialize();
        }


        public void Dispose()
        {
            _updatesQueue.NewItemsEvent -= UpdatesQueueNewItemsHandler;
            _updatesQueue?.Dispose();

            foreach (var sensor in _sensors.Values)
                if (sensor is IBarSensor barModel && barModel.LocalLastValue != default)
                    _databaseCore.AddSensorValue(barModel.LocalLastValue.ToEntity(sensor.Id));

            _databaseCore.Dispose();
        }


        public List<ProductModel> GetTree() => _tree.Values.ToList();

        public List<BaseSensorModel> GetSensors() => _sensors.Values.ToList();

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

        public List<ProductModel> GetProducts(User user, bool isAllProducts = false)
        {
            var products = _tree.Values.ToList();
            if (!isAllProducts)
                products = products.Where(p => p.ParentProduct == null).ToList();

            if (user == null || user.IsAdmin)
                return products;

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return new List<ProductModel>();

            var availableProducts = products.Where(p => ProductRoleHelper.IsAvailable(p.Id, user.ProductsRoles)).ToList();

            return isAllProducts ? GetAllProductsWithTheirSubProducts(availableProducts) : availableProducts;
        }

        public bool TryCheckKeyPermissions(StoreInfo storeInfo, out string message)
        {
            (string key, string path, _) = storeInfo;

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(path))
            {
                message = ErrorPathKey;
                return false;
            }

            if (!IsInitialized)
            {
                message = NotInitializedCacheError;
                return false;
            }

            var parts = path.Split(CommonConstants.SensorPathSeparator);
            if (parts.Contains(string.Empty))
            {
                message = ErrorInvalidPath;
                return false;
            }
            else if (parts.Length > ConfigurationConstants.DefaultMaxPathLength) // TODO : get maxPathLength from IConfigurationProvider
            {
                message = ErrorTooLongPath;
                return false;
            }

            if (!TryGetProductByKey(key, out var product, out message))
                return false;
            else if (product.Id == key)
                return true;

            // TODO: remove after refactoring sensors data storing
            if (product.ParentProduct is not null)
            {
                message = "Temporarily unavailable feature. Please select a product without a parent";
                return false;
            }

            var accessKey = GetAccessKeyModel(key);
            if (!accessKey.HasPermissionForSendData(out message))
                return false;

            if (accessKey.Permissions.HasFlag(KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors))
                return true;

            return IsValidKeyForPath(parts, product, accessKey, out message);
        }


        public AccessKeyModel AddAccessKey(AccessKeyModel key)
        {
            if (AddKeyToTree(key))
            {
                _databaseCore.AddAccessKey(key.ToAccessKeyEntity());

                ChangeAccessKeyEvent?.Invoke(key, TransactionType.Add);

                return key;
            }

            return null;
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

        public AccessKeyModel UpdateAccessKey(AccessKeyUpdate updatedKey)
        {
            if (!_keys.TryGetValue(updatedKey.Id, out var key))
                return null;

            key.Update(updatedKey);
            _databaseCore.UpdateAccessKey(key.ToAccessKeyEntity());

            ChangeAccessKeyEvent?.Invoke(key, TransactionType.Update);

            return key;
        }

        public AccessKeyModel GetAccessKey(Guid id) => _keys.GetValueOrDefault(id);

        public void UpdateSensor(SensorUpdate updatedSensor)
        {
            if (!_sensors.TryGetValue(updatedSensor.Id, out var sensor))
                return;

            sensor.Update(updatedSensor);
            UpdateIntervalPolicy(updatedSensor.ExpectedUpdateInterval, sensor);

            _databaseCore.UpdateSensor(sensor.ToEntity());
            OnChangeSensorEvent(sensor, TransactionType.Update);
        }

        public void RemoveSensor(Guid sensorId)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            if (_tree.TryGetValue(sensor.ProductId, out var parent))
                parent.Sensors.TryRemove(sensorId, out _);

            _databaseCore.RemoveSensorWithMetadata(sensorId.ToString(), sensor.ProductName, sensor.Path);

            OnChangeSensorEvent(sensor, TransactionType.Delete);
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

            sensor.ClearValues();
            _databaseCore.ClearSensorValues(sensor.Id.ToString(), sensor.ProductName, sensor.Path);

            OnChangeSensorEvent(sensor, TransactionType.Update);
        }

        public BaseSensorModel GetSensor(Guid sensorId) => _sensors.GetValueOrDefault(sensorId);

        public void OnChangeSensorEvent(BaseSensorModel model, TransactionType type) =>
            ChangeSensorEvent?.Invoke(model, type);


        public List<BaseValue> GetSensorValues(Guid sensorId, int count)
        {
            List<BaseValue> GetValues(BaseSensorModel sensor) => sensor.GetValues(count);

            (var sensor, var values) = GetCachedValues(sensorId, GetValues);

            int remainingCount = count - values.Count;
            if (remainingCount > 0)
            {
                var oldestValueTime = values.LastOrDefault()?.ReceivingTime.AddTicks(-1) ?? DateTime.MaxValue;
                values.AddRange(sensor.ConvertValues(
                    _databaseCore.GetSensorValues(sensorId.ToString(), sensor.ProductName, sensor.Path, oldestValueTime, remainingCount)));
            }

            return values;
        }

        public List<BaseValue> GetSensorValues(Guid sensorId, DateTime from, DateTime to)
        {
            List<BaseValue> GetValues(BaseSensorModel sensor) => sensor.GetValues(from, to);

            (var sensor, var values) = GetCachedValues(sensorId, GetValues);

            var oldestValueTime = values.LastOrDefault()?.ReceivingTime.AddTicks(-1) ?? to;
            values.AddRange(sensor.ConvertValues(
                _databaseCore.GetSensorValues(sensorId.ToString(), sensor.ProductName, sensor.Path, from, oldestValueTime)));

            return values;
        }

        public void UpdatePolicy(TransactionType type, Policy policy)
        {
            switch (type)
            {
                case TransactionType.Add:
                    _databaseCore.AddPolicy(policy.ToEntity());
                    return;
                case TransactionType.Update:
                    _databaseCore.UpdatePolicy(policy.ToEntity());
                    return;
                case TransactionType.Delete:
                    _databaseCore.RemovePolicy(policy.Id);
                    return;
            }
        }

        private (BaseSensorModel sensor, List<BaseValue> values) GetCachedValues(Guid sensorId, Func<BaseSensorModel, List<BaseValue>> getValuesFunc)
        {
            var values = new List<BaseValue>(1 << 6);

            if (_sensors.TryGetValue(sensorId, out var sensor))
            {
                values.AddRange(getValuesFunc(sensor));
                values.Reverse();
            }

            return (sensor, values);
        }


        private void UpdatesQueueNewItemsHandler(IEnumerable<StoreInfo> storeInfos)
        {
            foreach (var store in storeInfos)
                AddNewSensorValue(store);
        }

        internal void AddNewSensorValue(StoreInfo storeInfo)
        {
            (string key, string path, BaseValue value) = storeInfo;

            if (!TryGetProductByKey(key, out var product, out _))
                return;

            var parentProduct = AddNonExistingProductsAndGetParentProduct(product, path);

            var sensorName = path.Split(CommonConstants.SensorPathSeparator)[^1];
            var sensor = parentProduct.Sensors.FirstOrDefault(s => s.Value.DisplayName == sensorName).Value;
            if (sensor == null)
            {
                SensorEntity entity = new()
                {
                    ProductId = parentProduct.Id,
                    DisplayName = sensorName,
                    Type = (byte)value.Type,
                };

                sensor = SensorModelFactory.Build(entity);
                parentProduct.AddSensor(sensor);

                AddSensor(sensor);
                UpdateProduct(parentProduct);
            }

            if (sensor.TryAddValue(value, out var cachedValue) && cachedValue != null)
                _databaseCore.AddSensorValue(cachedValue.ToEntity(sensor.Id));

            OnChangeSensorEvent(sensor, TransactionType.Update);
        }

        private void UpdateIntervalPolicy(TimeSpan newInterval, BaseSensorModel sensor)
        {
            var oldPolicy = sensor.ExpectedUpdateIntervalPolicy;

            if (oldPolicy == null && newInterval != TimeSpan.Zero)
            {
                var newPolicy = new ExpectedUpdateIntervalPolicy(newInterval.Ticks);
                sensor.ExpectedUpdateIntervalPolicy = newPolicy;
                UpdatePolicy(TransactionType.Add, newPolicy);
            }
            else if (oldPolicy != null)
            {
                if (newInterval == TimeSpan.Zero)
                {
                    sensor.ExpectedUpdateIntervalPolicy = null;
                    UpdatePolicy(TransactionType.Delete, oldPolicy);
                }
                else if (newInterval.Ticks != oldPolicy.ExpectedUpdateInterval)
                {
                    oldPolicy.ExpectedUpdateInterval = newInterval.Ticks;
                    UpdatePolicy(TransactionType.Update, oldPolicy);
                }
            }
        }

        private void Initialize()
        {
            _logger.Info($"{nameof(TreeValuesCache)} is initializing");

            var productEntities = RequestProducts();
            ApplyProducts(productEntities);

            ApplySensors(productEntities, RequestSensors(), RequestPolicies());

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _databaseCore.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys(accessKeysEntities.ToList());
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            IsInitialized = true;

            _logger.Info($"{nameof(TreeValuesCache)} initialized");
        }

        private List<ProductEntity> RequestProducts()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} is requesting");
            var productEntities = _databaseCore.GetAllProducts();
            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} requested");

            return productEntities;
        }

        private List<SensorEntity> RequestSensors()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} is requesting");
            var sensorEntities = _databaseCore.GetAllSensors();
            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} requested");

            return sensorEntities;
        }

        private List<byte[]> RequestPolicies()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllPolicies)} is requesting");
            var policyEntities = _databaseCore.GetAllPolicies();
            _logger.Info($"{nameof(IDatabaseCore.GetAllPolicies)} requested");

            return policyEntities;
        }

        private void ApplyProducts(List<ProductEntity> productEntities)
        {
            _logger.Info($"{nameof(productEntities)} are applying");
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                _tree.TryAdd(product.Id, product);
            }
            _logger.Info($"{nameof(productEntities)} applied");

            _logger.Info("Links between products are building");
            foreach (var productEntity in productEntities)
                if (_tree.TryGetValue(productEntity.Id, out var product))
                {
                    if (productEntity.SubProductsIds != null)
                        foreach (var subProductId in productEntity.SubProductsIds)
                        {
                            if (_tree.TryGetValue(subProductId, out var subProduct))
                                product.AddSubProduct(subProduct);
                        }
                }
            _logger.Info("Links between products are built");

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (productEntities.Count == 0 || monitoringProduct == null)
                AddSelfMonitoringProduct();
        }

        private void ApplySensors(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities, List<byte[]> policyEntities)
        {
            _logger.Info($"{nameof(policyEntities)} are deserializing");
            var policies = GetPolicyModels(policyEntities);
            _logger.Info($"{nameof(policyEntities)} deserialized");

            _logger.Info($"{nameof(sensorEntities)} are applying");
            ApplySensors(sensorEntities, policies);
            _logger.Info($"{nameof(sensorEntities)} applied");

            _logger.Info("Links between products and their sensors are building");
            foreach (var productEntity in productEntities)
                if (_tree.TryGetValue(productEntity.Id, out var product))
                {
                    if (productEntity.SensorsIds != null)
                        foreach (var sensorId in productEntity.SensorsIds)
                        {
                            if (_sensors.TryGetValue(Guid.Parse(sensorId), out var sensor))
                                product.AddSensor(sensor);
                        }
                }
            _logger.Info("Links between products and their sensors are built");

            _logger.Info($"{nameof(TreeValuesCache.FillSensorsData)} is started");
            FillSensorsData();
            _logger.Info($"{nameof(TreeValuesCache.FillSensorsData)} is finished");
        }

        private void ApplySensors(List<SensorEntity> entities, Dictionary<Guid, Policy> policies)
        {
            foreach (var entity in entities)
            {
                try
                {
                    var sensor = GetSensorModel(entity);
                    _sensors.TryAdd(sensor.Id, sensor);

                    foreach (var policyId in entity.Policies)
                        if (policies.TryGetValue(Guid.Parse(policyId), out var policy))
                            sensor.AddPolicy(policy);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Applying sensor {entity.Id} error: {ex.Message}");
                }
            }
        }

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

        private ProductModel AddNonExistingProductsAndGetParentProduct(ProductModel parentProduct, string sensorPath)
        {
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

        private void AddSensor(BaseSensorModel sensor)
        {
            if (_tree.TryGetValue(sensor.ProductId, out var product))
                sensor.BuildProductNameAndPath(product);

            if (sensor is StringSensorModel)
                AddStringValueLengthPolicy(sensor);

            _sensors.TryAdd(sensor.Id, sensor);
            _databaseCore.AddSensor(sensor.ToEntity());

            OnChangeSensorEvent(sensor, TransactionType.Add);
        }

        private void AddStringValueLengthPolicy(BaseSensorModel sensor)
        {
            var policy = new StringValueLengthPolicy();

            sensor.AddPolicy(policy);
            _databaseCore.AddPolicy(policy.ToEntity());
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

        private static bool IsValidKeyForPath(string[] parts, ProductModel product,
            AccessKeyModel accessKey, out string message)
        {
            message = string.Empty;

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
                    if (!product.Sensors.Any(s => s.Value.DisplayName == expectedName) &&
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

        private BaseSensorModel GetSensorModel(SensorEntity entity)
        {
            var sensor = SensorModelFactory.Build(entity);

            if (_tree.TryGetValue(sensor.ProductId, out var product))
                sensor.BuildProductNameAndPath(product);

            return sensor;
        }

        private void FillSensorsData()
        {
            var sensorValues = _databaseCore.GetLatestValues(GetSensors());

            foreach (var (sensorId, value) in sensorValues)
                if (_sensors.TryGetValue(sensorId, out var sensor))
                    sensor.AddValue(value);
        }

        private static Dictionary<Guid, Policy> GetPolicyModels(List<byte[]> policyEntities)
        {
            Dictionary<Guid, Policy> policies = new(policyEntities.Count);

            var serializeOptions = new JsonSerializerOptions();
            serializeOptions.Converters.Add(new PolicyDeserializationConverter());

            foreach (var entity in policyEntities)
            {
                var policy = JsonSerializer.Deserialize<Policy>(entity, serializeOptions);
                policies.Add(policy.Id, policy);
            }

            return policies;
        }
    }
}
