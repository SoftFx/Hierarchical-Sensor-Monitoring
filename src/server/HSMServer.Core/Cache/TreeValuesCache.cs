using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache, IDisposable
    {
        private const string NotInitializedCacheError = "Cache is not initialized yet.";
        private const string NotExistingSensor = "Sensor with your path does not exist.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";

        public const int MaxHistoryCount = 50000;

        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys = new();
        private readonly ConcurrentDictionary<Guid, ProductModel> _tree = new();

        private readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly IDatabaseCore _databaseCore;
        private readonly IUpdatesQueue _updatesQueue;


        public bool IsInitialized { get; }

        public event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;
        public event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        public event Action<ProductModel, ActionType> ChangeProductEvent;

        public event Action<BaseSensorModel, PolicyResult> NotifyAboutChangesEvent;


        public TreeValuesCache(IDatabaseCore databaseCore, IUpdatesQueue updatesQueue)
        {
            _databaseCore = databaseCore;

            _updatesQueue = updatesQueue;
            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            Initialize();

            IsInitialized = true;
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

        public ProductModel AddProduct(string productName) => AddProduct(new ProductModel(productName));

        public void UpdateProduct(ProductModel product)
        {
            _databaseCore.UpdateProduct(product.ToProductEntity());

            ChangeProductEvent?.Invoke(product, ActionType.Update);
        }

        public void UpdateProduct(ProductUpdate update)
        {
            if (!_tree.TryGetValue(update.Id, out var product))
                return;

            var sensorsOldStatuses = new Dictionary<Guid, PolicyResult>();

            GetProductSensorsStatuses(product, sensorsOldStatuses);

            _databaseCore.UpdateProduct(product.Update(update).ToProductEntity());

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

                ChangeProductEvent?.Invoke(product, ActionType.Delete);
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

        /// <returns>list of root products (without parent)</returns>
        public List<ProductModel> GetProducts() =>
            _tree.Values.Where(p => p.ParentProduct == null).ToList();

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
                message = $"Sensor {sensor.RootProductName}{sensor.Path} is blocked.";
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

                ChangeAccessKeyEvent?.Invoke(key, ActionType.Add);

                return key;
            }

            return null;
        }

        public AccessKeyModel RemoveAccessKey(Guid id)
        {
            if (_keys.TryRemove(id, out var key))
            {
                if (_tree.TryGetValue(key.ProductId, out var product))
                {
                    product.AccessKeys.TryRemove(id, out _);
                    ChangeProductEvent?.Invoke(product, ActionType.Update);
                }

                _databaseCore.RemoveAccessKey(id);

                ChangeAccessKeyEvent?.Invoke(key, ActionType.Delete);
            }

            return key;
        }

        public AccessKeyModel UpdateAccessKey(AccessKeyUpdate updatedKey)
        {
            if (!_keys.TryGetValue(updatedKey.Id, out var key))
                return null;

            key.Update(updatedKey);
            _databaseCore.UpdateAccessKey(key.ToAccessKeyEntity());

            ChangeAccessKeyEvent?.Invoke(key, ActionType.Update);

            return key;
        }

        public AccessKeyModel UpdateAccessKeyState(Guid id, KeyState updatedState)
        {
            if (!_keys.TryGetValue(id, out var key))
                return null;

            return UpdateAccessKey(new AccessKeyUpdate(key.Id, updatedState));
        }

        public AccessKeyModel GetAccessKey(Guid id) => _keys.GetValueOrDefault(id);

        public void UpdateSensor(SensorUpdate update)
        {
            if (!_sensors.TryGetValue(update.Id, out var sensor))
                return;

            var oldStatus = sensor.Status;

            sensor.Update(update);

            _databaseCore.UpdateSensor(sensor.ToEntity());
            NotifyAboutChanges(sensor, oldStatus);
        }

        public void RemoveSensor(Guid sensorId)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            if (_tree.TryGetValue(sensor.ParentProduct.Id, out var parent))
                parent.Sensors.TryRemove(sensorId, out _);

            _databaseCore.RemoveSensorWithMetadata(sensorId.ToString());

            ChangeSensorEvent?.Invoke(sensor, ActionType.Delete);
        }

        public void UpdateMutedSensorState(Guid sensorId, DateTime? endOfMuting = null)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor) || sensor.State == SensorState.Blocked)
                return;

            if (sensor.EndOfMuting != endOfMuting)
                UpdateSensor(new SensorUpdate
                {
                    Id = sensorId,
                    State = endOfMuting is null ? SensorState.Available : SensorState.Muted,
                    EndOfMutingPeriod = endOfMuting,
                });
        }

        public void RemoveNode(Guid productId)
        {
            if (!_tree.TryGetValue(productId, out var product))
                return;

            foreach (var (subProductId, _) in product.SubProducts)
                RemoveNode(subProductId);

            foreach (var (sensorId, _) in product.Sensors)
                RemoveSensor(sensorId);

            RemoveProduct(product.Id);
        }

        public void ClearNodeHistory(Guid productId)
        {
            if (!_tree.TryGetValue(productId, out var product))
                return;

            foreach (var (subProductId, _) in product.SubProducts)
                ClearNodeHistory(subProductId);

            foreach (var (sensorId, _) in product.Sensors)
                ClearSensorHistory(sensorId);
        }

        public void ClearSensorHistory(Guid sensorId)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor))
                return;

            sensor.ResetSensor();
            _databaseCore.ClearSensorValues(sensor.Id.ToString());

            ChangeSensorEvent?.Invoke(sensor, ActionType.Update);
        }

        public BaseSensorModel GetSensor(Guid sensorId) => _sensors.GetValueOrDefault(sensorId);

        public void NotifyAboutChanges(BaseSensorModel sensor, PolicyResult oldStatus)
        {
            NotifyAboutChangesEvent?.Invoke(sensor, oldStatus);
            ChangeSensorEvent?.Invoke(sensor, ActionType.Update);
        }


        public IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request)
        {
            var sensorId = GetSensor(request).Id;
            var count = request.Count switch
            {
                > 0 => Math.Min(request.Count.Value, MaxHistoryCount),
                < 0 => Math.Max(request.Count.Value, -MaxHistoryCount),
                _ => MaxHistoryCount
            };

            return count > 0
                ? GetSensorValuesPage(sensorId, request.From, request.To ?? DateTime.UtcNow.AddDays(1), count)
                : GetSensorValuesPage(sensorId, DateTime.MinValue, request.From, count);
        }

        public async IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count)
        {
            if (_sensors.TryGetValue(sensorId, out var sensor))
            {
                var pages = _databaseCore.GetSensorValuesPage(sensorId.ToString(), from, to, count);

                await foreach (var page in pages)
                    yield return sensor.ConvertValues(page);
            }
        }

        private void UpdatePolicy(ActionType type, Policy policy)
        {
            switch (type)
            {
                case ActionType.Add:
                    _databaseCore.AddPolicy(policy.ToEntity());
                    return;
                case ActionType.Update:
                    _databaseCore.UpdatePolicy(policy.ToEntity());
                    return;
                case ActionType.Delete:
                    _databaseCore.RemovePolicy(policy.Id);
                    return;
            }
        }


        private void SubscribeToPolicyUpdate(ServerPolicyCollection collection)
        {
            foreach (var serverPolicy in collection)
                serverPolicy.Uploaded += UpdatePolicy;
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
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = sensorName,
                    Type = (byte)value.Type,
                };

                sensor = SensorModelFactory.Build(entity).InitDataPolicy();
                parentProduct.AddSensor(sensor);

                AddSensor(sensor);
                UpdateProduct(parentProduct);
            }
            else if (sensor.State == SensorState.Blocked)
                return;

            var oldStatus = sensor.Status;

            if (sensor.TryAddValue(value) && sensor.LastDbValue != null)
                _databaseCore.AddSensorValue(sensor.LastDbValue.ToEntity(sensor.Id));

            NotifyAboutChanges(sensor, oldStatus);
        }

        private void NotifyAllProductChildrenAboutUpdate(ProductModel product,
            Dictionary<Guid, PolicyResult> sensorsOldStatuses)
        {
            ChangeProductEvent(product, ActionType.Update);

            foreach (var (_, sensor) in product.Sensors)
                if (sensorsOldStatuses.TryGetValue(sensor.Id, out var oldStatus))
                    NotifyAboutChanges(sensor, oldStatus);
                else
                    ChangeSensorEvent(sensor, ActionType.Update);

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

            ApplySensors(productEntities, RequestSensors(), policies);

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _databaseCore.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys(accessKeysEntities.ToList());
            _logger.Info($"{nameof(accessKeysEntities)} applied");

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

        private void ApplyProducts(List<ProductEntity> productEntities, Dictionary<string, Policy> policies)
        {
            _logger.Info($"{nameof(productEntities)} are applying");
            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);
                product.ApplyPolicies(productEntity.Policies, policies);

                _tree.TryAdd(product.Id, product);
            }

            _logger.Info($"{nameof(productEntities)} applied");

            _logger.Info("Links between products are building");
            foreach (var productEntity in productEntities)
                if (!string.IsNullOrEmpty(productEntity.ParentProductId))
                {
                    var parentId = Guid.Parse(productEntity.ParentProductId);
                    var productId = Guid.Parse(productEntity.Id);

                    if (_tree.TryGetValue(parentId, out var parent) && _tree.TryGetValue(productId, out var product))
                        parent.AddSubProduct(product);
                }
            _logger.Info("Links between products are built");
        }

        private void ApplySensors(List<ProductEntity> productEntities, List<SensorEntity> sensorEntities,
            Dictionary<string, Policy> policies)
        {
            _logger.Info($"{nameof(sensorEntities)} are applying");
            ApplySensors(sensorEntities, policies);
            _logger.Info($"{nameof(sensorEntities)} applied");

            _logger.Info("Links between products and their sensors are building");
            foreach (var sensorEntity in sensorEntities)
                if (!string.IsNullOrEmpty(sensorEntity.ProductId))
                {
                    var parentId = Guid.Parse(sensorEntity.ProductId);
                    var sensorId = Guid.Parse(sensorEntity.Id);

                    if (_tree.TryGetValue(parentId, out var parent) && _sensors.TryGetValue(sensorId, out var sensor))
                        parent.AddSensor(sensor);
                }
            _logger.Info("Links between products and their sensors are built");

            _logger.Info($"{nameof(TreeValuesCache.FillSensorsData)} is started");
            FillSensorsData();
            _logger.Info($"{nameof(TreeValuesCache.FillSensorsData)} is finished");
        }

        private void ApplySensors(List<SensorEntity> entities, Dictionary<string, Policy> policies)
        {
            foreach (var entity in entities)
            {
                try
                {
                    var sensor = SensorModelFactory.Build(entity);
                    sensor.ApplyPolicies(entity.Policies, policies);

                    _sensors.TryAdd(sensor.Id, sensor);

                    SubscribeToPolicyUpdate(sensor.ServerPolicy);
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
                AddKeyToTree(new AccessKeyModel(keyEntity));

            foreach (var product in _tree.Values)
            {
                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
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

        private ProductModel AddProduct(ProductModel product)
        {
            if (_tree.TryAdd(product.Id, product))
            {
                SubscribeToPolicyUpdate(product.ServerPolicy);

                _databaseCore.AddProduct(product.ToProductEntity());

                ChangeProductEvent?.Invoke(product, ActionType.Add);

                foreach (var (_, key) in product.AccessKeys)
                    AddAccessKey(key);

                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
            }

            return product;
        }

        private void AddSensor(BaseSensorModel sensor)
        {
            _sensors.TryAdd(sensor.Id, sensor);
            _databaseCore.AddSensor(sensor.ToEntity());

            SubscribeToPolicyUpdate(sensor.ServerPolicy);

            ChangeSensorEvent?.Invoke(sensor, ActionType.Add);
        }

        private bool AddKeyToTree(AccessKeyModel key)
        {
            bool isSuccess = _keys.TryAdd(key.Id, key);

            if (isSuccess && _tree.TryGetValue(key.ProductId, out var product))
            {
                isSuccess &= product.AccessKeys.TryAdd(key.Id, key);
                ChangeProductEvent?.Invoke(product, ActionType.Update);
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
                        !TryCheckAccessKeyPermissions(accessKey,
                            KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors, out message))
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

        private static bool TryCheckAccessKeyPermissions(AccessKeyModel accessKey, KeyPermissions permissions,
            out string message)
        {
            if (accessKey == null)
            {
                message = NotExistingSensor;
                return false;
            }

            return accessKey.IsHasPermissions(permissions, out message);
        }

        private static void GetProductSensorsStatuses(ProductModel product, Dictionary<Guid, PolicyResult> sensorsStatuses)
        {
            foreach (var (sensorId, sensor) in product.Sensors)
                sensorsStatuses.Add(sensorId, sensor.Status);

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
                    sensor.TryAddValue(value);
        }

        private Dictionary<string, Policy> GetPolicyModels(List<byte[]> policyEntities)
        {
            Dictionary<string, Policy> policies = new(policyEntities.Count);

            foreach (var entity in policyEntities) //TODO: remove migration
            {
                var obj = JsonSerializer.Deserialize<JsonObject>(entity);
                var isOldObj = obj["Type"] != null;

                if (isOldObj)
                {
                    var newObj = new JsonObject();

                    var value = obj["Type"].AsValue().ToString();

                    newObj["$type"] = value switch
                    {
                        "ExpectedUpdateIntervalPolicy" => 1000,
                        "StringValueLengthPolicy" => 2000,
                    };

                    foreach (var node in obj)
                        if (node.Key != "Type")
                            newObj[node.Key] = JsonNode.Parse(node.Value.ToJsonString());

                    obj = newObj;
                }

                var policy = JsonSerializer.Deserialize<Policy>(obj);
                policies.Add(policy.Id.ToString(), policy);

                if (isOldObj)
                    UpdatePolicy(ActionType.Update, policy);
            }

            return policies;
        }

        public void UpdateCacheState()
        {
            foreach (var sensor in GetSensors())
            {
                var oldStatus = sensor.Status;

                if (sensor.HasUpdateTimeout())
                    NotifyAboutChanges(sensor, oldStatus);
            }

            foreach (var key in GetAccessKeys())
                if (key.IsExpired && key.State < KeyState.Expired)
                    UpdateAccessKeyState(key.Id, KeyState.Expired);

            foreach (var sensor in GetSensors())
                if (sensor.EndOfMuting <= DateTime.UtcNow)
                    UpdateMutedSensorState(sensor.Id);
        }
    }
}