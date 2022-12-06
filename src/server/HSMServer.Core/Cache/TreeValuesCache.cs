using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Requests;
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
        private const string ErrorKeyNotFound = "Key doesn't exist.";
        private const string NotInitializedCacheError = "Cache is not initialized yet.";
        private const string NotExistingSensor = "Sensor with your path does not exist.";

        private static readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IDatabaseCore _databaseCore;
        private readonly IUserManager _userManager;
        private readonly IUpdatesQueue _updatesQueue;

        private readonly ConcurrentDictionary<Guid, ProductModel> _tree;
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors;
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys;

        [Obsolete]
        private readonly ConcurrentDictionary<string, Guid> _productKeys = new();

        public bool IsInitialized { get; private set; }

        public event Action<ProductModel, TransactionType> ChangeProductEvent;
        public event Action<BaseSensorModel, TransactionType> ChangeSensorEvent;
        public event Action<AccessKeyModel, TransactionType> ChangeAccessKeyEvent;

        public event Action<BaseSensorModel, ValidationResult> NotifyAboutChangesEvent;


        public TreeValuesCache(IDatabaseCore databaseCore, IUserManager userManager, IUpdatesQueue updatesQueue)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;

            _updatesQueue = updatesQueue;
            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            _tree = new ConcurrentDictionary<Guid, ProductModel>();
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

        public void UpdateProduct(ProductModel product)
        {
            _databaseCore.UpdateProduct(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, TransactionType.Update);
        }

        public void UpdateProduct(ProductUpdate updatedProduct)
        {
            if (!_tree.TryGetValue(updatedProduct.Id, out var product))
                return;

            var sensorsOldStatuses = new Dictionary<Guid, ValidationResult>();
            GetProductSensorsStatuses(product, sensorsOldStatuses);

            UpdateIntervalPolicy(updatedProduct.ExpectedUpdateInterval, product);

            _databaseCore.UpdateProduct(product.ToProductEntity());
            NotifyAllProductChildrenAboutUpdate(product, sensorsOldStatuses);
        }

        public void RemoveProduct(Guid productId)
        {
            void RemoveProduct(Guid productId)
            {
                if (!_tree.TryRemove(productId, out var product))
                    return;

                foreach (var (subProductId, _) in product.SubProducts)
                    RemoveProduct(subProductId);

                foreach (var (sensorId, _) in product.Sensors)
                    RemoveSensor(sensorId);

                product.ParentProduct?.SubProducts.TryRemove(productId, out _);
                _databaseCore.RemoveProduct(product.Id.ToString());

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

        public ProductModel GetProduct(Guid id) => _tree.GetValueOrDefault(id);

        /// <returns>product (without parent) with name = name</returns>
        public ProductModel GetProductByName(string name) =>
            _tree.FirstOrDefault(p => p.Value.ParentProduct == null && p.Value.DisplayName == name).Value;

        public string GetProductNameById(Guid id) => GetProduct(id)?.DisplayName;

        public List<ProductModel> GetProducts(User user, bool isAllProducts = false)
        {
            var products = _tree.Values.ToList();
            if (!isAllProducts)
                products = products.Where(p => p.ParentProduct == null).ToList();

            if (user == null || user.IsAdmin)
                return products;

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return new List<ProductModel>();

            var availableProducts = products.Where(p => user.IsProductAvailable(p.Id)).ToList();

            return isAllProducts ? GetAllProductsWithTheirSubProducts(availableProducts) : availableProducts;
        }

        public bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message)
        {
            if (!TryCheckCacheInitialization(out message) ||
                !TryGetProductByKey(request, out var product, out message))
                return false;

            // TODO: remove after refactoring sensors data storing
            if (product.ParentProduct is not null)
            {
                message = "Temporarily unavailable feature. Please select a product without a parent";
                return false;
            }

            var accessKey = GetAccessKeyModel(request);
            if (!accessKey.IsValid(KeyPermissions.CanSendSensorData, out message))
                return false;

            var sensorChecking = TryGetSensor(request, product, accessKey, out var sensor, out message);

            if (sensor?.State == SensorState.Blocked)
            {
                message = $"Sensor {CommonConstants.BuildPath(sensor.RootProductName, sensor.Path)} is blocked.";
                return false;
            }

            return sensorChecking;
        }

        public bool TryCheckKeyReadPermissions(BaseRequestModel request, out string message) =>
            TryCheckCacheInitialization(out message) &&
            TryGetProductByKey(request, out var product, out message) &&
            GetAccessKeyModel(request).IsValid(KeyPermissions.CanReadSensorData, out message) &&
            TryGetSensor(request, product, null, out _, out message);

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

            if (_tree.TryGetValue(key.ProductId, out var product))
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

            var oldStatus = sensor.ValidationResult;

            sensor.Update(updatedSensor);
            UpdateIntervalPolicy(updatedSensor.ExpectedUpdateInterval, sensor);

            _databaseCore.UpdateSensor(sensor.ToEntity());
            NotifyAboutChanges(sensor, oldStatus);
        }

        public void RemoveSensor(Guid sensorId)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            if (_tree.TryGetValue(sensor.ParentProduct.Id, out var parent))
                parent.Sensors.TryRemove(sensorId, out _);

            _databaseCore.RemoveSensorWithMetadata(sensorId.ToString(), sensor.RootProductName, sensor.Path);
            _userManager.RemoveSensorFromUsers(sensorId);

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Delete);
        }

        public void RemoveSensorsData(Guid productId)
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
            _databaseCore.ClearSensorValues(sensor.Id.ToString(), sensor.RootProductName, sensor.Path);

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }

        public BaseSensorModel GetSensor(Guid sensorId) => _sensors.GetValueOrDefault(sensorId);

        public void NotifyAboutChanges(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            NotifyAboutChangesEvent?.Invoke(sensor, oldStatus);
            ChangeSensorEvent?.Invoke(sensor, TransactionType.Update);
        }


        public List<BaseValue> GetSensorValues(Guid sensorId, int count)
        {
            List<BaseValue> GetValues(BaseSensorModel sensor) => sensor.GetValues(count);

            (var sensor, var values) = GetCachedValues(sensorId, GetValues);
            if (sensor == null)
                return values;

            int remainingCount = count - values.Count;
            if (remainingCount > 0)
            {
                var oldestValueTime = values.LastOrDefault()?.ReceivingTime.AddTicks(-1) ?? DateTime.MaxValue;
                values.AddRange(sensor.ConvertValues(
                    _databaseCore.GetSensorValues(sensorId.ToString(), sensor.RootProductName, sensor.Path, oldestValueTime, remainingCount)));
            }

            return values;
        }

        public List<BaseValue> GetSensorValues(Guid sensorId, DateTime from, DateTime to, int count = 50000)
        {
            List<BaseValue> GetValues(BaseSensorModel sensor) => sensor.GetValues(from, to);

            (var sensor, var values) = GetCachedValues(sensorId, GetValues);
            if (sensor == null)
                return values;

            var oldestValueTime = values.LastOrDefault()?.ReceivingTime.AddTicks(-1) ?? to;
            values.AddRange(sensor.ConvertValues(
                _databaseCore.GetSensorValues(sensorId.ToString(), sensor.RootProductName, sensor.Path, from, oldestValueTime, count)));

            return values;
        }

        public List<BaseValue> GetSensorValues(HistoryRequestModel request)
        {
            var sensor = GetSensor(request);
            var historyValues = request.To.HasValue
                ? GetSensorValues(sensor.Id, request.From, request.To.Value)
                : GetSensorValues(sensor.Id, request.From, DateTime.UtcNow.AddDays(1), int.MaxValue).TakeLast(request.Count.Value).ToList();

            return historyValues;
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
            if (!TryGetProductByKey(storeInfo, out var product, out _))
                return;

            var parentProduct = AddNonExistingProductsAndGetParentProduct(product, storeInfo);

            var value = storeInfo.BaseValue;
            var sensorName = storeInfo.PathParts[^1];
            var sensor = parentProduct.Sensors.FirstOrDefault(s => s.Value.DisplayName == sensorName).Value;

            if (sensor == null)
            {
                SensorEntity entity = new()
                {
                    DisplayName = sensorName,
                    Type = (byte)value.Type,
                };

                sensor = SensorModelFactory.Build(entity);
                parentProduct.AddSensor(sensor);

                AddSensor(sensor);
                UpdateProduct(parentProduct);
            }
            else if (sensor.State == SensorState.Blocked)
                return;

            var oldStatus = sensor.ValidationResult;

            if (sensor.TryAddValue(value, out var cachedValue) && cachedValue != null)
                _databaseCore.AddSensorValue(cachedValue.ToEntity(sensor.Id));

            NotifyAboutChanges(sensor, oldStatus);
        }

        private void UpdateIntervalPolicy(TimeIntervalModel newInterval, NodeBaseModel node)
        {
            if (newInterval == null)
                return;

            var oldPolicy = node.ExpectedUpdateInterval;

            if (oldPolicy == null && !newInterval.IsEmpty)
            {
                var newPolicy = new ExpectedUpdateIntervalPolicy(newInterval.CustomPeriod, newInterval.TimeInterval);
                node.ExpectedUpdateInterval = newPolicy;

                UpdatePolicy(TransactionType.Add, newPolicy);
            }
            else if (oldPolicy != null)
            {
                if (newInterval.IsEmpty)
                {
                    node.ExpectedUpdateInterval = null;

                    UpdatePolicy(TransactionType.Delete, oldPolicy);
                }
                else if (!oldPolicy.IsEqual(newInterval))
                {
                    oldPolicy.Update(newInterval);

                    UpdatePolicy(TransactionType.Update, oldPolicy);
                }
            }

            node.RefreshOutdatedError();
        }

        private void NotifyAllProductChildrenAboutUpdate(ProductModel product, Dictionary<Guid, ValidationResult> sensorsOldStatuses)
        {
            ChangeProductEvent(product, TransactionType.Update);

            foreach (var (_, sensor) in product.Sensors)
                if (sensorsOldStatuses.TryGetValue(sensor.Id, out var oldStatus))
                    NotifyAboutChanges(sensor, oldStatus);
                else
                    ChangeSensorEvent(sensor, TransactionType.Update);

            foreach (var (_, subProduct) in product.SubProducts)
                NotifyAllProductChildrenAboutUpdate(subProduct, sensorsOldStatuses);
        }

        private void Initialize()
        {
            _logger.Info($"{nameof(TreeValuesCache)} is initializing");

            var policyEntities = RequestPolicies();

            _logger.Info($"{nameof(policyEntities)} are deserializing");
            var policies = GetPolicyModels(policyEntities);
            _logger.Info($"{nameof(policyEntities)} deserialized");

            var productEntities = RequestProducts();
            ApplyProducts(productEntities, policies);

            UsersMigration();

            ApplySensors(productEntities, RequestSensors(), policies);

            BuildNodesProductNameAndPath();

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _databaseCore.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys(accessKeysEntities.ToList());
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            IsInitialized = true;

            _logger.Info($"{nameof(TreeValuesCache)} initialized");
        }

        private void BuildNodesProductNameAndPath()
        {
            _logger.Info("Path and ProductName properties are building for nodes");

            foreach (var (_, product) in _tree)
                if (product.ParentProduct == null)
                    product.BuildProductNameAndPath();

            _logger.Info("Path and ProductName properties are built for nodes");
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

        private void ApplyProducts(List<ProductEntity> productEntities, Dictionary<Guid, Policy> policies)
        {
            _logger.Info($"{nameof(productEntities)} are applying");
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                product.ApplyPolicies(productEntity.Policies, policies);

                _tree.TryAdd(product.Id, product);

                if (productEntity.Id != product.Id.ToString())
                    _productKeys.TryAdd(productEntity.Id, product.Id);
            }
            _logger.Info($"{nameof(productEntities)} applied");

            _logger.Info("Links between products are building");
            foreach (var productEntity in productEntities)
                if (!string.IsNullOrEmpty(productEntity.ParentProductId))
                {
                    var parentId = _productKeys.TryGetValue(productEntity.ParentProductId, out var mappedParentId)
                        ? mappedParentId
                        : Guid.Parse(productEntity.ParentProductId);
                    var productId = _productKeys.TryGetValue(productEntity.Id, out var mappedProductId)
                        ? mappedProductId
                        : Guid.Parse(productEntity.Id);

                    if (_tree.TryGetValue(parentId, out var parent) && _tree.TryGetValue(productId, out var product))
                        parent.AddSubProduct(product);
                }
            _logger.Info("Links between products are built");
        }

        private void ApplySensors(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities, Dictionary<Guid, Policy> policies)
        {
            _logger.Info($"{nameof(sensorEntities)} are applying");
            ApplySensors(sensorEntities, policies);
            _logger.Info($"{nameof(sensorEntities)} applied");

            _logger.Info("Links between products and their sensors are building");
            foreach (var sensorEntity in sensorEntities)
                if (!string.IsNullOrEmpty(sensorEntity.ProductId))
                {
                    var parentId = _productKeys.TryGetValue(sensorEntity.ProductId, out var mappedParentId)
                        ? mappedParentId
                        : Guid.Parse(sensorEntity.ProductId);
                    var sensorId = Guid.Parse(sensorEntity.Id);

                    if (_tree.TryGetValue(parentId, out var parent) && _sensors.TryGetValue(sensorId, out var sensor))
                        parent.AddSensor(sensor);
                }
                else { }
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
                    var sensor = SensorModelFactory.Build(entity);
                    sensor.ApplyPolicies(entity.Policies, policies);

                    _sensors.TryAdd(sensor.Id, sensor);
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
                Guid? parentId = _productKeys.TryGetValue(keyEntity.ProductId, out var mappedId) ? mappedId : null;

                AddKeyToTree(new AccessKeyModel(keyEntity, parentId));
            }

            foreach (var product in _tree.Values)
            {
                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
            }
        }

        private void UsersMigration()
        {
            var users = _userManager.GetUsers();
            var usersToResave = new List<User>();

            foreach (var user in users)
            {
                bool needToResave = false;

                for (int i = 0; i < user.ProductsRoles.Count; ++i)
                {
                    var role = user.ProductsRoles[i];

                    if (_productKeys.TryGetValue(role.Key, out var mappedId))
                    {
                        user.ProductsRoles[i] = new KeyValuePair<string, ProductRoleEnum>(mappedId.ToString(), role.Value);
                        needToResave = true;
                    }
                }

                if (needToResave)
                    usersToResave.Add(user);
            }
        }

        private ProductModel AddNonExistingProductsAndGetParentProduct(ProductModel parentProduct, BaseRequestModel request)
        {
            var pathParts = request.PathParts;

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

        private void AddProduct(ProductModel product)
        {
            product.BuildProductNameAndPath();

            if (_tree.TryAdd(product.Id, product))
            {
                _databaseCore.AddProduct(product.ToProductEntity());

                ChangeProductEvent?.Invoke(product, TransactionType.Add);

                foreach (var (_, key) in product.AccessKeys)
                    AddAccessKey(key);

                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
            }
        }

        private void AddSensor(BaseSensorModel sensor)
        {
            sensor.BuildProductNameAndPath();

            if (sensor is StringSensorModel)
                AddStringValueLengthPolicy(sensor);

            _sensors.TryAdd(sensor.Id, sensor);
            _databaseCore.AddSensor(sensor.ToEntity());

            ChangeSensorEvent?.Invoke(sensor, TransactionType.Add);
        }

        private void AddStringValueLengthPolicy(BaseSensorModel sensor)
        {
            var policy = new StringValueLengthPolicy();

            sensor.AddPolicy(policy);
            _databaseCore.AddPolicy(policy.ToEntity());
        }

        private bool AddKeyToTree(AccessKeyModel key)
        {
            bool isSuccess = _keys.TryAdd(key.Id, key);

            if (isSuccess && _tree.TryGetValue(key.ProductId, out var product))
            {
                isSuccess &= product.AddAccessKey(key);
                ChangeProductEvent?.Invoke(product, TransactionType.Update);
            }

            return isSuccess;
        }

        private bool TryCheckCacheInitialization(out string message)
        {
            message = IsInitialized ? string.Empty : NotInitializedCacheError;

            return string.IsNullOrEmpty(message);
        }

        private bool TryGetProductByKey(BaseRequestModel request, out ProductModel product, out string message)
        {
            var keyModel = GetAccessKeyModel(request);
            if (keyModel == AccessKeyModel.InvalidKey)
            {
                message = ErrorKeyNotFound;
                product = null;
                return false;
            }

            var hasProduct = _tree.TryGetValue(keyModel.ProductId, out product);
            message = hasProduct ? string.Empty : ErrorKeyNotFound;

            return hasProduct;
        }

        private static bool TryGetSensor(BaseRequestModel request, ProductModel product,
            AccessKeyModel accessKey, out BaseSensorModel sensor, out string message)
        {
            message = string.Empty;
            sensor = null;
            var parts = request.PathParts;

            for (int i = 0; i < parts.Length; i++)
            {
                var expectedName = parts[i];

                if (i != parts.Length - 1)
                {
                    product = product?.SubProducts.FirstOrDefault(sp => sp.Value.DisplayName == expectedName).Value;

                    if (product == null &&
                        !TryCheckAccessKeyPermissions(accessKey, KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors, out message))
                        return false;
                }
                else
                {
                    sensor = product?.Sensors.FirstOrDefault(s => s.Value.DisplayName == expectedName).Value;

                    if (sensor == null &&
                        !TryCheckAccessKeyPermissions(accessKey, KeyPermissions.CanAddSensors, out message))
                        return false;
                }
            }

            return true;
        }

        private static bool TryCheckAccessKeyPermissions(AccessKeyModel accessKey, KeyPermissions permissions, out string message)
        {
            if (accessKey == null)
            {
                message = NotExistingSensor;
                return false;
            }

            return accessKey.IsHasPermissions(permissions, out message);
        }

        private static List<ProductModel> GetAllProductsWithTheirSubProducts(List<ProductModel> products)
        {
            var productsWithTheirSubProducts = new Dictionary<Guid, ProductModel>(products.Count);
            foreach (var product in products)
            {
                productsWithTheirSubProducts.Add(product.Id, product);
                GetAllProductSubProducts(product, productsWithTheirSubProducts);
            }

            return productsWithTheirSubProducts.Values.ToList();
        }

        private static void GetAllProductSubProducts(ProductModel product, Dictionary<Guid, ProductModel> allSubProducts)
        {
            foreach (var (subProductId, subProduct) in product.SubProducts)
            {
                if (!allSubProducts.ContainsKey(subProductId))
                    allSubProducts.Add(subProductId, subProduct);

                GetAllProductSubProducts(subProduct, allSubProducts);
            }
        }

        private static void GetProductSensorsStatuses(ProductModel product, Dictionary<Guid, ValidationResult> sensorsStatuses)
        {
            foreach (var (sensorId, sensor) in product.Sensors)
                sensorsStatuses.Add(sensorId, sensor.ValidationResult);

            foreach (var (_, subProduct) in product.SubProducts)
                GetProductSensorsStatuses(subProduct, sensorsStatuses);
        }

        private BaseSensorModel GetSensor(BaseRequestModel request)
        {
            if (TryGetProductByKey(request, out var product, out _) &&
                TryGetSensor(request, product, null, out var sensor, out _))
                return sensor;

            return null;
        }

        private AccessKeyModel GetAccessKeyModel(BaseRequestModel request) =>
            _keys.TryGetValue(request.KeyGuid, out var keyModel)
                ? keyModel
                : AccessKeyModel.InvalidKey;

        private void FillSensorsData()
        {
            var sensorValues = _databaseCore.GetLatestValues(GetSensors());

            foreach (var (sensorId, value) in sensorValues)
                if (value is not null && _sensors.TryGetValue(sensorId, out var sensor))
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
