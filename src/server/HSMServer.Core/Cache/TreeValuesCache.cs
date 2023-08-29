using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TreeStateSnapshot;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache, IDisposable
    {
        private const string NotInitializedCacheError = "Cache is not initialized yet.";
        private const string NotExistingSensor = "Sensor with your path does not exist.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";
        private const string ErrorMasterKey = "Master key is invalid for this request because product is not specified.";

        public const int MaxHistoryCount = 50000;
        public const string System = "System";

        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys = new();
        private readonly ConcurrentDictionary<Guid, ProductModel> _tree = new();

        private readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly ITreeStateSnapshot _snapshot;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly IJournalService _journalService;
        private readonly IDatabaseCore _database;


        public event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;
        public event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        public event Action<ProductModel, ActionType> ChangeProductEvent;
        public event Action<PolicyResult> ChangePolicyResultEvent;


        public TreeValuesCache(IDatabaseCore database, ITreeStateSnapshot snapshot, IUpdatesQueue updatesQueue, IJournalService journalService)
        {
            _database = database;
            _snapshot = snapshot;

            _updatesQueue = updatesQueue;
            _journalService = journalService;
            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            Initialize();
        }


        public void SaveLastStateToDb()
        {
            foreach (var sensor in _sensors.Values)
                if (sensor is IBarSensor barModel && barModel.LocalLastValue != default)
                    SaveSensorValueToDb(barModel.LocalLastValue, sensor.Id);
        }

        public void Dispose()
        {
            _updatesQueue.NewItemsEvent -= UpdatesQueueNewItemsHandler;
            _updatesQueue?.Dispose();


            _database.Dispose();
        }


        public List<ProductModel> GetAllNodes() => _tree.Values.ToList();

        public List<BaseSensorModel> GetSensors() => _sensors.Values.ToList();

        public List<AccessKeyModel> GetAccessKeys() => _keys.Values.ToList();

        public ProductModel AddProduct(string productName, Guid authorId) => AddProduct(new ProductModel(productName, authorId));

        private void UpdateProduct(ProductModel product)
        {
            _database.UpdateProduct(product.ToEntity());

            ChangeProductEvent?.Invoke(product, ActionType.Update);
        }

        public void UpdateProduct(ProductUpdate update)
        {
            if (!_tree.TryGetValue(update.Id, out var product))
                return;

            _database.UpdateProduct(product.Update(update).ToEntity());

            NotifyAboutProductChange(product);
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

                RemoveBaseNodeSubscription(product);

                product.Parent?.SubProducts.TryRemove(productId, out _);
                _database.RemoveProduct(product.Id.ToString());

                foreach (var (id, _) in product.AccessKeys)
                    RemoveAccessKey(id);

                RemoveEntityPolicies(product);

                ChangeProductEvent?.Invoke(product, ActionType.Delete);
            }

            if (_tree.TryGetValue(productId, out var product))
            {
                RemoveProduct(productId);

                if (product.Parent != null)
                    UpdateProduct(product.Parent);
            }
        }

        public ProductModel GetProduct(Guid id) => _tree.GetValueOrDefault(id);

        /// <returns>product (without parent) with name = name</returns>
        public ProductModel GetProductByName(string name) =>
            _tree.FirstOrDefault(p => p.Value.Parent == null && p.Value.DisplayName == name).Value;

        public string GetProductNameById(Guid id) => GetProduct(id)?.DisplayName;

        /// <returns>list of root products (without parent)</returns>
        public List<ProductModel> GetProducts() => _tree.Values.Where(p => p.Parent == null).ToList();

        public bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message)
        {
            if (!TryGetProductByKey(request, out var product, out message))
                return false;

            // TODO: remove after refactoring sensors data storing
            if (product.Parent is not null)
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
            TryGetProductByKey(request, out var product, out message) &&
            GetAccessKeyModel(request).IsValid(KeyPermissions.CanReadSensorData, out message) &&
            TryGetSensor(request, product, null, out _, out message);

        public AccessKeyModel AddAccessKey(AccessKeyModel key)
        {
            if (AddKeyToTree(key))
            {
                _database.AddAccessKey(key.ToAccessKeyEntity());

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

                _database.RemoveAccessKey(id);

                ChangeAccessKeyEvent?.Invoke(key, ActionType.Delete);
            }

            return key;
        }

        public AccessKeyModel UpdateAccessKey(AccessKeyUpdate updatedKey)
        {
            if (!_keys.TryGetValue(updatedKey.Id, out var key))
                return null;

            key.Update(updatedKey);
            _database.UpdateAccessKey(key.ToAccessKeyEntity());

            ChangeAccessKeyEvent?.Invoke(key, ActionType.Update);

            return key;
        }

        public AccessKeyModel UpdateAccessKeyState(Guid id, KeyState newState)
        {
            return !_keys.TryGetValue(id, out var key) ? null : UpdateAccessKey(new AccessKeyUpdate(key.Id, newState));
        }

        public AccessKeyModel GetAccessKey(Guid id) => _keys.GetValueOrDefault(id);

        public List<AccessKeyModel> GetMasterKeys() => GetAccessKeys().Where(x => x.IsMaster).ToList();


        public void UpdateSensor(SensorUpdate update)
        {
            if (!_sensors.TryGetValue(update.Id, out var sensor))
                return;

            sensor.Update(update);
            _database.UpdateSensor(sensor.ToEntity());

            SensorUpdateView(sensor);
        }

        public void RemoveSensor(Guid sensorId, string initiator = null)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            if (sensor.Parent is not null && _tree.TryGetValue(sensor.Parent.Id, out var parent))
            {
                parent.Sensors.TryRemove(sensorId, out _);
                _journalService.RemoveRecords(sensorId, parent.Id);

                if (initiator is not null)
                    _journalService.AddRecord(new JournalRecordModel(parent.Id, initiator)
                    {
                        Enviroment = "Remove sensor",
                        Path = sensor.FullPath,
                    });
            }
            else
                _journalService.RemoveRecords(sensorId);

            RemoveSensorPolicies(sensor);

            _database.RemoveSensorWithMetadata(sensorId.ToString());
            _snapshot.Sensors.Remove(sensorId);

            ChangeSensorEvent?.Invoke(sensor, ActionType.Delete);
        }

        public void UpdateMutedSensorState(Guid sensorId, DateTime? endOfMuting = null, string initiator = null)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor) || sensor.State is SensorState.Blocked)
                return;

            if (sensor.EndOfMuting != endOfMuting)
                UpdateSensor(new SensorUpdate
                {
                    Id = sensorId,
                    State = endOfMuting is null ? SensorState.Available : SensorState.Muted,
                    EndOfMutingPeriod = endOfMuting,
                    Initiator = initiator
                });
        }

        public void ClearNodeHistory(ClearHistoryRequest request)
        {
            if (!_tree.TryGetValue(request.Id, out var product))
                return;

            foreach (var (subProductId, _) in product.SubProducts)
                ClearNodeHistory(request with { Id = subProductId });

            foreach (var (sensorId, _) in product.Sensors)
                ClearSensorHistory(request with { Id = sensorId });
        }

        public void CheckSensorHistory(Guid sensorId)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor))
                return;

            var from = _snapshot.Sensors[sensorId].History.From;
            var policy = sensor.Settings.KeepHistory.Value;

            if (policy.TimeIsUp(from))
                ClearSensorHistory(new(sensorId, policy.GetShiftedTime(DateTime.UtcNow, -1)));
        }

        public void ClearSensorHistory(ClearHistoryRequest request)
        {
            if (!_sensors.TryGetValue(request.Id, out var sensor))
                return;

            var from = _snapshot.Sensors[request.Id].History.From;

            if (from > request.To)
                return;

            sensor.Storage.Clear(request.To);

            if (!sensor.HasData)
                sensor.ResetSensor();

            _database.ClearSensorValues(sensor.Id.ToString(), from, request.To);
            _snapshot.Sensors[request.Id].History.From = request.To;

            SensorUpdateView(sensor);
        }


        public BaseSensorModel GetSensor(Guid sensorId) => _sensors.GetValueOrDefault(sensorId);


        public void SendPolicyResult(BaseSensorModel sensor, PolicyResult? policy = null)
        {
            if (sensor.State != SensorState.Muted)
                ChangePolicyResultEvent?.Invoke(policy ?? sensor.PolicyResult);
        }

        public void SensorUpdateView(BaseSensorModel sensor) => ChangeSensorEvent?.Invoke(sensor, ActionType.Update);


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
                   ? GetSensorValuesPage(sensorId, request.From, request.To ?? DateTime.UtcNow.AddDays(1), count, request.Options)
                   : GetSensorValuesPage(sensorId, DateTime.MinValue, request.From, count, request.Options);
        }

        public async IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            static bool IsNotTimout(BaseValue value) => !value.IsTimeout;

            if (_sensors.TryGetValue(sensorId, out var sensor))
            {
                var pages = _database.GetSensorValuesPage(sensorId, from, to, count);
                var includeTtl = options.HasFlag(RequestOptions.IncludeTtl);

                await foreach (var page in pages)
                {
                    var convertedValues = sensor.ConvertValues(page);

                    yield return (includeTtl ? convertedValues : convertedValues.Where(IsNotTimout)).ToList();
                }
            }
        }


        public void AddNewChat(Guid chatId, string name, string productName)
        {
            foreach (var (_, sensor) in _sensors)
                if (productName is null || sensor.RootProductName == productName)
                {
                    foreach (var policy in sensor.Policies)
                        if (policy.Destination.AllChats && !policy.Destination.Chats.ContainsKey(chatId))
                        {
                            policy.Destination.Chats.Add(chatId, name);
                            policy.RebuildState();

                            UpdatePolicy(ActionType.Update, policy);
                        }

                    if (sensor.Policies.TimeToLive.AddChat(chatId, name))
                        UpdatePolicy(ActionType.Update, sensor.Policies.TimeToLive);
                }

            foreach (var (_, product) in _tree)
                if (productName is null || product.RootProductName == productName)
                    if (product.Policies.TimeToLive.AddChat(chatId, name))
                        UpdatePolicy(ActionType.Update, product.Policies.TimeToLive);
        }

        public void RemoveChat(Guid chatId, string productName)
        {
            foreach (var (_, sensor) in _sensors)
                if (productName is null || sensor.RootProductName == productName)
                {
                    foreach (var policy in sensor.Policies)
                        if (policy.Destination.Chats.Remove(chatId))
                        {
                            policy.RebuildState();

                            UpdatePolicy(ActionType.Update, policy);
                        }

                    if (sensor.Policies.TimeToLive.RemoveChat(chatId))
                        UpdatePolicy(ActionType.Update, sensor.Policies.TimeToLive);
                }

            foreach (var (_, product) in _tree)
                if (productName is null || product.RootProductName == productName)
                    if (product.Policies.TimeToLive.RemoveChat(chatId))
                        UpdatePolicy(ActionType.Update, product.Policies.TimeToLive);
        }

        private void UpdatePolicy(ActionType type, Policy policy)
        {
            switch (type)
            {
                case ActionType.Add:
                    _database.AddPolicy(policy.ToEntity());
                    return;
                case ActionType.Update:
                    _database.UpdatePolicy(policy.ToEntity());
                    return;
                case ActionType.Delete:
                    _database.RemovePolicy(policy.Id);
                    return;
            }
        }

        private void AddBaseNodeSubscription(BaseNodeModel model)
        {
            model.Settings.ChangesHandler += _journalService.AddRecord;
            model.ChangesHandler += _journalService.AddRecord;
        }

        private void RemoveBaseNodeSubscription(BaseNodeModel model)
        {
            model.Settings.ChangesHandler -= _journalService.AddRecord;
            model.ChangesHandler -= _journalService.AddRecord;
        }

        private void SubscribeSensorToPolicyUpdate(BaseSensorModel sensor)
        {
            sensor.Policies.ChangesHandler += _journalService.AddRecord;
            sensor.Policies.SensorExpired += SetExpiredSnapshot;
            sensor.Policies.Uploaded += UpdatePolicy;

            sensor.UpdateFromParentSettings += _database.UpdateSensor;

            AddBaseNodeSubscription(sensor);
        }

        private void RemoveSensorPolicies(BaseSensorModel sensor)
        {
            sensor.Policies.ChangesHandler -= _journalService.AddRecord;
            sensor.Policies.SensorExpired -= SetExpiredSnapshot;
            sensor.Policies.Uploaded -= UpdatePolicy;

            sensor.UpdateFromParentSettings -= _database.UpdateSensor;

            RemoveBaseNodeSubscription(sensor);
            RemoveEntityPolicies(sensor);
            RemoveEntityPolicies(sensor);
        }

        private void RemoveEntityPolicies(BaseNodeModel entity)
        {
            foreach (var policyId in entity.Policies.Ids)
                _database.RemovePolicy(policyId);
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

                sensor = SensorModelFactory.Build(entity);
                parentProduct.AddSensor(sensor);
                if (!sensor.Settings.TTL.IsSet)
                    sensor.Policies.TimeToLive.ApplyParent(parentProduct.Policies.TimeToLive);

                SubscribeSensorToPolicyUpdate(sensor);

                var root = GetProductByName(sensor.RootProductName); //should be after AddSensor because of subscription
                sensor.Policies.AddDefaultSensors(root?.NotificationsSettings?.TelegramSettings?.Chats?.ToDictionary(u => new Guid(u.SystemId), v => v.Name));

                AddSensor(sensor);
                UpdateProduct(parentProduct);
            }
            else if (sensor.State == SensorState.Blocked)
                return;

            var oldStatus = sensor.Status;

            if (sensor.TryAddValue(value) && sensor.LastDbValue != null)
                SaveSensorValueToDb(sensor.LastDbValue, sensor.Id);

            if (!sensor.PolicyResult.IsOk)
                SendPolicyResult(sensor);

            SensorUpdateView(sensor);
        }

        private void SaveSensorValueToDb(BaseValue value, Guid sensorId)
        {
            _database.AddSensorValue(value.ToEntity(sensorId));

            if (!value.IsTimeout)
                _snapshot.Sensors[sensorId].SetLastUpdate(value.ReceivingTime);
        }

        private void NotifyAboutProductChange(ProductModel product)
        {
            ChangeProductEvent?.Invoke(product, ActionType.Update);

            foreach (var (_, sensor) in product.Sensors)
                SensorUpdateView(sensor);

            foreach (var (_, subProduct) in product.SubProducts)
                NotifyAboutProductChange(subProduct);
        }

        private void Initialize()
        {
            _logger.Info($"{nameof(TreeValuesCache)} is initializing");

            var policies = RequestPolicies();

            var productEntities = RequestProducts();
            ApplyProducts(productEntities);

            var sensorEntities = RequestSensors();

            ApplySensors(productEntities, sensorEntities, policies);

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _database.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys(accessKeysEntities.ToList());
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            _logger.Info($"{nameof(TreeValuesCache)} initialized");

            PoliciesDestinationMigration();

            UpdateCacheState();
        }

        [Obsolete("Should be removed after policies chats migration")]
        private void PoliciesDestinationMigration()
        {
            _logger.Info($"Starting policies destination migration for products...");

            var productsToResave = new HashSet<Guid>(_tree.Count);

            var productsChats = new Dictionary<string, Dictionary<Guid, string>>();
            foreach (var product in GetProducts())
            {
                var chats = new Dictionary<Guid, string>();
                if (product?.NotificationsSettings?.TelegramSettings?.Chats?.Count > 0)
                    foreach (var chat in product.NotificationsSettings.TelegramSettings.Chats)
                        chats.Add(new Guid(chat.SystemId), chat.Name);

                productsChats.Add(product.DisplayName, chats);
            }

            foreach (var (productId, product) in _tree)
            {
                var policy = product.Policies.TimeToLive;
                if (policy.Destination is not null)
                    continue;

                var ttlUpdate = new PolicyUpdate
                {
                    Id = policy.Id,
                    Conditions = policy.Conditions.Select(u => new PolicyConditionUpdate(u.Operation, u.Property, u.Target, u.Combination)).ToList(),
                    Destination = new PolicyDestinationUpdate(true, productsChats.TryGetValue(product.RootProductName, out var chats) ? chats : new()),
                    Sensitivity = policy.Sensitivity,
                    Status = policy.Status,
                    Template = policy.Template,
                    IsDisabled = policy.IsDisabled,
                    Icon = policy.Icon,
                };

                product.Policies.UpdateTTL(ttlUpdate);
                productsToResave.Add(productId);
            }

            foreach (var productId in productsToResave)
                if (_tree.TryGetValue(productId, out var product))
                    _database.UpdateProduct(product.ToEntity());

            _logger.Info($"{productsToResave.Count} polices destination migration is finished for products");
        }

        [Obsolete("Should be removed after policies chats migration")]
        public void UpdatePolicy(Policy policy)
        {
            policy.RebuildState();
            _database.UpdatePolicy(policy.ToEntity());
        }

        [Obsolete("Should be removed after policies chats migration")]
        public void UpdateSensor(Guid sensorId)
        {
            if (_sensors.TryGetValue(sensorId, out var sensor))
                _database.UpdateSensor(sensor.ToEntity());
        }


        private List<ProductEntity> RequestProducts()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} is requesting");
            var productEntities = _database.GetAllProducts();
            _logger.Info($"{nameof(IDatabaseCore.GetAllProducts)} requested");

            return productEntities;
        }

        private List<SensorEntity> RequestSensors()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} is requesting");
            var sensorEntities = _database.GetAllSensors();
            _logger.Info($"{nameof(IDatabaseCore.GetAllSensors)} requested");

            return sensorEntities;
        }

        private Dictionary<string, PolicyEntity> RequestPolicies()
        {
            _logger.Info($"{nameof(IDatabaseCore.GetAllPolicies)} is requesting");
            var policyEntities = _database.GetAllPolicies();
            _logger.Info($"{nameof(IDatabaseCore.GetAllPolicies)} requested");

            //TODO should be removed after migration
            for (int i = 0; i < policyEntities.Count; ++i)
                if (policyEntities[i].Conditions.Count == 1)
                {
                    var entity = policyEntities[i];
                    var cond = entity.Conditions[0];
                    var template = entity.Template;

                    if (cond.Property == (byte)PolicyProperty.Status && cond.Operation == (byte)PolicyOperation.IsChanged && template.StartsWith("$status"))
                    {
                        var newEntity = entity with
                        {
                            Template = $"$prevStatus->{template}"
                        };

                        _database.UpdatePolicy(newEntity);
                        policyEntities[i] = newEntity;
                    }
                }

            return policyEntities.ToDictionary(k => new Guid(k.Id).ToString(), v => v);
        }

        [Obsolete("Should be removed after telegram chat IDs migration")]
        private void TelegramChatsMigration()
        {
            _logger.Info($"Starting products telegram chats migration...");

            var productsToResave = new HashSet<Guid>();
            var chatIds = new Dictionary<long, byte[]>(1 << 4);

            foreach (var (_, node) in _tree)
                if (node.NotificationsSettings?.TelegramSettings?.Chats is not null)
                    foreach (var chat in node.NotificationsSettings.TelegramSettings.Chats)
                        if (chat.SystemId is null)
                        {
                            if (chatIds.TryGetValue(chat.Id, out var systemChatId))
                                chat.SystemId = systemChatId;
                            else
                            {
                                chat.SystemId = Guid.NewGuid().ToByteArray();
                                chatIds.Add(chat.Id, chat.SystemId);
                            }

                            productsToResave.Add(node.Id);
                        }

            foreach (var productId in productsToResave)
                if (_tree.TryGetValue(productId, out var product))
                    _database.UpdateProduct(product.ToEntity());

            _logger.Info($"{productsToResave.Count} products telegram chats migration is finished");
        }

        private void ApplyProducts(List<ProductEntity> productEntities)
        {
            _logger.Info($"{nameof(productEntities)} are applying");

            foreach (var productEntity in productEntities)
            {
                var product = new ProductModel(productEntity);

                AddBaseNodeSubscription(product);
                _tree.TryAdd(product.Id, product);
            }

            _logger.Info($"{nameof(productEntities)} applied");

            TelegramChatsMigration();

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
            Dictionary<string, PolicyEntity> policies)
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

            _logger.Info($"{nameof(FillSensorsData)} is started");
            FillSensorsData();
            _logger.Info($"{nameof(FillSensorsData)} is finished");
        }

        private void ApplySensors(List<SensorEntity> entities, Dictionary<string, PolicyEntity> policies)
        {
            foreach (var entity in entities)
            {
                try
                {
                    var sensor = SensorModelFactory.Build(entity);
                    sensor.Policies.ApplyPolicies(entity.Policies, policies);

                    _sensors.TryAdd(sensor.Id, sensor);
                    SubscribeSensorToPolicyUpdate(sensor);
                    SensorUpdateView(sensor);
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
            var authorId = GetAccessKey(request.KeyGuid).AuthorId;

            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                var subProductName = pathParts[i];
                var subProduct = parentProduct.SubProducts.FirstOrDefault(p => p.Value.DisplayName == subProductName).Value;
                if (subProduct == null)
                {
                    subProduct = new ProductModel(subProductName, authorId);

                    parentProduct.AddSubProduct(subProduct);
                    if (!subProduct.Settings.TTL.IsSet)
                        subProduct.Policies.TimeToLive.ApplyParent(parentProduct.Policies.TimeToLive);

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
                if (product.Parent == null)
                {
                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        TTL = new TimeIntervalModel(TimeInterval.None),
                        KeepHistory = new TimeIntervalModel(TimeInterval.Month),
                        SelfDestroy = new TimeIntervalModel(TimeInterval.Month),
                    };

                    product.Update(update);
                }

                AddBaseNodeSubscription(product);
                _database.AddProduct(product.ToEntity());

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
            _database.AddSensor(sensor.ToEntity());

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

        private bool TryGetProductByKey(BaseRequestModel request, out ProductModel product, out string message)
        {
            product = null;

            var keyModel = GetAccessKeyModel(request);

            if (keyModel == AccessKeyModel.InvalidKey)
            {
                message = ErrorKeyNotFound;
                return false;
            }

            if (keyModel.IsMaster)
            {
                message = ErrorMasterKey;
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

        private BaseSensorModel GetSensor(BaseRequestModel request)
        {
            if (TryGetProductByKey(request, out var product, out _) &&
                TryGetSensor(request, product, null, out var sensor, out _))
                return sensor;

            return null;
        }

        private AccessKeyModel GetAccessKeyModel(BaseRequestModel request) =>
            _keys.TryGetValue(request.KeyGuid, out var keyModel) ? keyModel : AccessKeyModel.InvalidKey;

        private void FillSensorsData()
        {
            void ApplyLastValues(Dictionary<Guid, byte[]> lasts)
            {
                foreach (var (sensorId, value) in lasts)
                    if (value is not null && _sensors.TryGetValue(sensorId, out var sensor))
                    {
                        sensor.AddDbValue(value);

                        if (!_snapshot.IsFinal && sensor.LastValue is not null)
                            _snapshot.Sensors[sensorId].SetLastUpdate(sensor.LastValue.ReceivingTime, sensor.CheckTimeout());
                    }
            }

            if (_snapshot.IsFinal)
            {
                var requests = GetSensors().ToDictionary(k => k.Id, _ => 0L);

                foreach (var (key, state) in _snapshot.Sensors)
                    if (requests.ContainsKey(key))
                        requests[key] = state.History.To.Ticks;

                ApplyLastValues(_database.GetLatestValues(requests));
            }
            else
            {
                var maxTo = DateTime.MaxValue.Ticks;
                var requests = GetSensors().ToDictionary(k => k.Id, _ => (0L, maxTo));

                if (_snapshot.HasData)
                {
                    foreach (var (key, state) in _snapshot.Sensors)
                        if (requests.ContainsKey(key))
                            requests[key] = (state.History.To.Ticks, maxTo);
                }

                ApplyLastValues(_database.GetLatestValuesFromTo(requests));

                requests.Clear();

                foreach (var sensor in GetSensors())
                    if (sensor.LastTimeout is not null)
                    {
                        _snapshot.Sensors[sensor.Id].IsExpired = true;

                        var fromVal = _snapshot.Sensors.TryGetValue(sensor.Id, out var state) ? state.History.To.Ticks : 0L;

                        requests.Add(sensor.Id, (fromVal, sensor.LastTimeout.ReceivingTime.Ticks));
                    }

                ApplyLastValues(_database.GetLatestValuesFromTo(requests));

                _snapshot.FlushState(true);
            }
        }

        public void UpdateCacheState()
        {
            foreach (var sensor in GetSensors())
                sensor.CheckTimeout();

            foreach (var key in GetAccessKeys())
                if (key.IsExpired && key.State < KeyState.Expired)
                    UpdateAccessKeyState(key.Id, KeyState.Expired);

            foreach (var sensor in GetSensors())
                if (sensor.EndOfMuting <= DateTime.UtcNow)
                    UpdateMutedSensorState(sensor.Id);
        }

        private void SetExpiredSnapshot(BaseSensorModel sensor, bool timeout)
        {
            var snapshot = _snapshot.Sensors[sensor.Id];

            if (snapshot.IsExpired != timeout)
            {
                var ttl = sensor.Policies.TimeToLive;
                snapshot.IsExpired = timeout;

                if (timeout)
                {
                    var value = sensor.GetTimeoutValue();

                    if ((sensor.LastTimeout is null || sensor.LastTimeout.ReceivingTime < sensor.LastUpdate) && sensor.TryAddValue(value))
                        SaveSensorValueToDb(value, sensor.Id);
                }

                SendPolicyResult(sensor, timeout ? ttl.PolicyResult : ttl.Ok);
            }

            SensorUpdateView(sensor);
        }
    }
}