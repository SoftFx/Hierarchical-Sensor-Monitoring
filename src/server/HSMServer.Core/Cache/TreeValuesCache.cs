using HSMCommon.Collections;
using HSMCommon.Constants;
using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Confirmation;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.TreeStateSnapshot;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache, IDisposable
    {
        private const string NotInitializedCacheError = "Cache is not initialized yet.";
        private const string NotExistingSensor = "Sensor with your path does not exist.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";
        private const string ErrorMasterKey = "Master key is invalid for this request because product is not specified.";

        public const int MaxHistoryCount = 50000;

        private readonly static MigrationManager _migrator = new();

        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys = new();
        private readonly ConcurrentDictionary<Guid, ProductModel> _tree = new();

        private readonly CDict<bool> _fileHistoryLocks = new(); // TODO: get file history should be fixed without this crutch

        private readonly Logger _logger = LogManager.GetLogger(CommonConstants.InfrastructureLoggerName);

        private readonly ConfirmationManager _confirmationManager = new();
        private readonly ScheduleManager _scheduleManager = new();

        private readonly ITreeStateSnapshot _snapshot;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly IJournalService _journalService;
        private readonly IDatabaseCore _database;


        public event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;
        public event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        public event Action<ProductModel, ActionType> ChangeProductEvent;

        public event Action<AlertMessage> NewAlertMessageEvent;


        public TreeValuesCache(IDatabaseCore database, ITreeStateSnapshot snapshot, IUpdatesQueue updatesQueue, IJournalService journalService)
        {
            _database = database;
            _snapshot = snapshot;

            _updatesQueue = updatesQueue;
            _journalService = journalService;

            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            _confirmationManager.NewMessageEvent += _scheduleManager.ProcessMessage;
            _scheduleManager.NewMessageEvent += SendAlertMessage;

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
            _confirmationManager.NewMessageEvent -= _scheduleManager.ProcessMessage;
            _scheduleManager.NewMessageEvent -= SendAlertMessage;

            _updatesQueue.NewItemsEvent -= UpdatesQueueNewItemsHandler;

            _updatesQueue.Dispose();
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

        public void RemoveProduct(Guid productId, InitiatorInfo initiator = null)
        {
            void RemoveProduct(Guid productId)
            {
                if (!_tree.TryRemove(productId, out var product))
                    return;

                foreach (var (subProductId, _) in product.SubProducts)
                    RemoveProduct(subProductId);

                foreach (var (sensorId, _) in product.Sensors)
                    RemoveSensor(sensorId, initiator, parentId: product.Parent?.Id);

                RemoveBaseNodeSubscription(product);

                product.Parent?.SubProducts.TryRemove(productId, out _);
                _database.RemoveProduct(product.Id.ToString());

                foreach (var (id, _) in product.AccessKeys)
                    RemoveAccessKey(id);

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

        public bool TryGetProductByName(string name, out ProductModel product)
        {
            product = GetProductByName(name);

            return product is not null;
        }

        public string GetProductNameById(Guid id) => GetProduct(id)?.DisplayName;

        /// <returns>list of root products (without parent)</returns>
        public List<ProductModel> GetProducts() => _tree.Values.Where(p => p.Parent == null).ToList();


        public bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message)
        {
            if (!TryCheckProductKey(request, out var product, out message))
                return false;

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

        public bool TryCheckSensorUpdateKeyPermission(BaseRequestModel request, out Guid sensorId, out string message)
        {
            sensorId = Guid.Empty;

            if (!TryCheckProductKey(request, out var product, out message))
                return false;

            var accessKey = GetAccessKeyModel(request);
            var sensorChecking = TryGetSensor(request, product, accessKey, out var sensor, out message);

            if (sensor is not null)
                sensorId = sensor.Id;

            return sensorChecking;
        }

        private bool TryCheckProductKey(BaseRequestModel request, out ProductModel product, out string message)
        {
            if (!TryGetProductByKey(request, out product, out message))
                return false;

            // TODO: remove after refactoring sensors data storing
            if (product.Parent is not null)
            {
                message = "Temporarily unavailable feature. Please select a product without a parent";
                return false;
            }

            return true;
        }


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


        public bool TryAddOrUpdateSensor(SensorAddOrUpdateRequestModel request, out string error)
        {
            var update = request.Update;

            if (update.Id == Guid.Empty)
            {
                if (!TryGetProductByKey(request, out var product, out _))
                {
                    error = $"Product with this key {request.KeyGuid} doesn't exists";
                    return false;
                }

                var parentProduct = AddNonExistingProductsAndGetParentProduct(product, request);
                var sensor = AddSensor(request, request.Type, parentProduct, request.Update.DefaultAlertsOptions);

                update = update with { Id = sensor.Id };
            }

            return TryUpdateSensor(update, out error);
        }

        public bool TryUpdateSensor(SensorUpdate update, out string error)
        {
            if (!_sensors.TryGetValue(update.Id, out var sensor))
            {
                error = "Sensor doesn't exist";
                return false;
            }

            sensor.TryUpdate(update, out error);
            _database.UpdateSensor(sensor.ToEntity());

            SensorUpdateView(sensor);

            return true;
        }

        public void UpdateSensorValue(UpdateSensorValueRequestModel request)
        {
            var sensor = GetSensor(request.Id);
            var lastValue = sensor.LastValue;

            if (request.Comment is not null && (!request.ChangeLast || lastValue is not null))
            {
                var value = request.BuildNewValue(sensor);
                var result = request.ChangeLast ? sensor.TryUpdateLastValue(value) : sensor.TryAddValue(value);

                if (result)
                {
                    var (oldValue, newValue) = request.GetValues(lastValue, sensor.LastValue); // value can be rebuild in storage so use LastValue

                    _journalService.AddRecord(new JournalRecordModel(request.Id, request.Initiator)
                    {
                        PropertyName = request.PropertyName,
                        Enviroment = request.Environment,
                        Path = sensor.FullPath,
                        OldValue = request.BuildComment(lastValue?.Status, lastValue?.Comment, oldValue),
                        NewValue = request.BuildComment(value: newValue)
                    });

                    if (sensor.LastDbValue != null)
                        SaveSensorValueToDb(sensor.LastDbValue, request.Id, true);

                    SensorUpdateViewAndNotify(sensor);
                }
            }
        }

        public void RemoveSensor(Guid sensorId, InitiatorInfo initiator = null, Guid? parentId = null)
        {
            if (!_sensors.TryRemove(sensorId, out var sensor))
                return;

            RemoveSensorPolicies(sensor); // should be before removing from parent

            if (sensor.Parent is not null && (_tree.TryGetValue(sensor.Parent.Id, out var parent) || parentId is not null))
            {
                parent?.RemoveSensor(sensorId);
                _journalService.RemoveRecords(sensorId, parentId ?? parent.Id);

                _journalService.AddRecord(new JournalRecordModel(parentId ?? parent.Id, initiator)
                {
                    Enviroment = "Remove sensor",
                    Path = sensor.FullPath,
                });
            }
            else
                _journalService.RemoveRecords(sensorId);

            _database.RemoveSensorWithMetadata(sensorId.ToString());
            _snapshot.Sensors.Remove(sensorId);

            ChangeSensorEvent?.Invoke(sensor, ActionType.Delete);
        }

        public void UpdateMutedSensorState(Guid sensorId, InitiatorInfo initiator, DateTime? endOfMuting = null)
        {
            if (!_sensors.TryGetValue(sensorId, out var sensor) || sensor.State is SensorState.Blocked)
                return;

            if (sensor.EndOfMuting != endOfMuting)
                TryUpdateSensor(new SensorUpdate
                {
                    Id = sensorId,
                    State = endOfMuting is null ? SensorState.Available : SensorState.Muted,
                    EndOfMutingPeriod = endOfMuting,
                    Initiator = initiator
                }, out _);
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
            var to = request.To;

            if (from > to)
                return;

            sensor.Storage.Clear(to);

            if (!sensor.HasData)
                sensor.ResetSensor();

            if (sensor.AggregateValues)
            {
                if (IsBorderedValue(sensor, from.Ticks - 1, out var latestFrom) && from <= latestFrom.LastUpdateTime && latestFrom.LastUpdateTime <= to)
                    from = latestFrom.ReceivingTime;

                if (IsBorderedValue(sensor, to.Ticks, out var latestTo))
                    to = latestTo.ReceivingTime.AddTicks(-1);

                if (from > to)
                    return;
            }

            _database.ClearSensorValues(sensor.Id.ToString(), from, to);
            _snapshot.Sensors[request.Id].History.From = to;

            SensorUpdateView(sensor);
        }

        private bool IsBorderedValue(BaseSensorModel sensor, long pointTicks, out BaseValue latest)
        {
            var bytes = _database.GetLatestValue(sensor.Id, pointTicks); // get prev pointTicks value

            latest = null;

            if (bytes is not null)
            {
                latest = sensor.Convert(bytes);

                return latest.ReceivingTime.Ticks <= pointTicks && pointTicks <= latest.LastUpdateTime.Ticks;
            }

            return false;
        }


        public BaseSensorModel GetSensor(Guid sensorId) => _sensors.GetValueOrDefault(sensorId);

        public IEnumerable<BaseSensorModel> GetSensorsByFolder(HashSet<Guid> folderIds = null)
        {
            bool GetAnySensor(BaseSensorModel _) => true;
            bool GetSensorByFolder(BaseSensorModel sensor) => folderIds.Contains(sensor.Root.FolderId ?? Guid.Empty);

            Predicate<BaseSensorModel> filter = folderIds switch
            {
                null => GetAnySensor,
                _ => GetSensorByFolder,
            };

            foreach (var (_, sensor) in _sensors)
                if (filter(sensor))
                    yield return sensor;
        }

        public bool TryGetSensorByPath(string productName, string path, out BaseSensorModel sensor)
        {
            sensor = null;

            var node = GetProductByName(productName);

            if (node is null)
                return false;

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var subNodeName in parts[..^1])
            {
                node = node.SubProducts.Values.FirstOrDefault(u => u.DisplayName == subNodeName);

                if (node is null)
                    return false;
            }

            sensor = node.Sensors.Values.FirstOrDefault(u => u.DisplayName == parts[^1]);

            return sensor is not null;
        }


        public void SendAlertMessage(AlertMessage message)
        {
            var sensorId = message.SensorId;

            if (_sensors.TryGetValue(sensorId, out var sensor) && sensor.CanSendNotifications)
            {
                var product = GetProductByName(sensor.RootProductName);

                if (product.FolderId.HasValue)
                    NewAlertMessageEvent?.Invoke(message.ApplyFolder(product));
            }
        }

        private void SensorUpdateViewAndNotify(BaseSensorModel sensor)
        {
            SensorUpdateView(sensor);
            SendNotification(sensor.Notifications);
        }

        private void SendNotification(PolicyResult result) => _confirmationManager.RegisterNotification(result);

        private void SensorUpdateView(BaseSensorModel sensor) => ChangeSensorEvent?.Invoke(sensor, ActionType.Update);


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

        private ValueTask<List<BaseValue>> GetSensorValues(Guid sensorId, SensorHistoryRequest request) =>
            GetSensorValuesPage(sensorId, request.From, request.To, request.Count, request.Options).Flatten();

        public async IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            bool IsNotTimout(BaseValue value) => !value.IsTimeout;


            if (_sensors.TryGetValue(sensorId, out var sensor))
            {
                if (sensor is FileSensorModel && _fileHistoryLocks[sensorId])
                    yield return new List<BaseValue>();
                else
                {
                    if (sensor is FileSensorModel)
                        _fileHistoryLocks[sensorId] = true;


                    var includeTtl = options.HasFlag(RequestOptions.IncludeTtl);

                    if (sensor.AggregateValues && IsBorderedValue(sensor, from.Ticks - 1, out var latest) && (includeTtl || IsNotTimout(latest)))
                        from = latest.ReceivingTime;

                    await foreach (var page in _database.GetSensorValuesPage(sensorId, from, to, count))
                    {
                        var convertedValues = sensor.Convert(page);

                        yield return (includeTtl ? convertedValues : convertedValues.Where(IsNotTimout)).ToList();
                    }


                    if (sensor is FileSensorModel)
                        _fileHistoryLocks[sensorId] = false;
                }
            }
        }

        public SensorHistoryInfo GetSensorHistoryInfo(Guid sensorId)
        {
            var (dataCnt, keysSize, valueSize) = _database.CalculateSensorHistorySize(sensorId);

            return new SensorHistoryInfo
            {
                ValuesSizeBytes = valueSize,
                KeysSizeBytes = keysSize,
                DataCount = dataCnt,
            };
        }

        public NodeHistoryInfo GetNodeHistoryInfo(Guid nodeId)
        {
            void CalculateHistoryInfo(ProductModel model, NodeHistoryInfo rootInfo)
            {
                if (model.IsEmpty)
                    return;

                foreach (var (sensorId, _) in model.Sensors)
                    rootInfo.SensorsInfo.Add(sensorId, GetSensorHistoryInfo(sensorId));

                foreach (var (nodeId, subNode) in model.SubProducts)
                {
                    var subNodeInfo = new NodeHistoryInfo();

                    CalculateHistoryInfo(subNode, subNodeInfo);

                    rootInfo.SubnodesInfo.Add(nodeId, subNodeInfo);
                }
            }


            var nodeInfo = new NodeHistoryInfo();

            if (_tree.TryGetValue(nodeId, out var nodeModel))
                CalculateHistoryInfo(nodeModel, nodeInfo);

            return nodeInfo;
        }


        public void RemoveChatsFromPolicies(Guid folderId, List<Guid> chats, InitiatorInfo initiator)
        {
            if (chats.Count == 0)
                return;

            var chatsHash = new HashSet<Guid>(chats);

            foreach (var product in GetProducts().Where(p => p.FolderId == folderId))
                RemoveChatsFromPolicies(product, chatsHash, initiator);
        }

        private void RemoveChatsFromPolicies(ProductModel product, HashSet<Guid> chats, InitiatorInfo initiator)
        {
            if (TryGetPolicyUpdate(product.Policies.TimeToLive, chats, initiator, out var productTtlUpdate))
            {
                var update = new ProductUpdate()
                {
                    Id = product.Id,
                    TTLPolicy = productTtlUpdate,
                    Initiator = initiator,
                };

                UpdateProduct(update);
            }

            foreach (var (_, sensor) in product.Sensors)
            {
                TryGetPolicyUpdate(sensor.Policies.TimeToLive, chats, initiator, out var sensorTtlUpdate);

                List<PolicyUpdate> policiesUpdate = null;
                if (sensor.Policies.Any(p => CanRemoveChatsFromPolicy(p.Destination, chats)))
                {
                    policiesUpdate = new(sensor.Policies.Count());

                    foreach (var policy in sensor.Policies)
                    {
                        if (!TryGetPolicyUpdate(policy, chats, initiator, out var policyUpdate))
                            policyUpdate = BuildPolicyUpdate(policy, new(policy.Destination.Chats, policy.Destination.AllChats), initiator);

                        policiesUpdate.Add(policyUpdate);
                    }
                }

                if (policiesUpdate is not null || sensorTtlUpdate is not null)
                {
                    var update = new SensorUpdate()
                    {
                        Id = sensor.Id,
                        Policies = policiesUpdate,
                        TTLPolicy = sensorTtlUpdate,
                        Initiator = initiator,
                    };

                    TryUpdateSensor(update, out _);
                }
            }

            foreach (var (_, subProduct) in product.SubProducts)
                RemoveChatsFromPolicies(subProduct, chats, initiator);
        }

        private static bool TryGetPolicyUpdate(Policy policy, HashSet<Guid> chats, InitiatorInfo initiator, out PolicyUpdate update)
        {
            update = null;

            var destination = policy.Destination;
            if (CanRemoveChatsFromPolicy(destination, chats))
            {
                var destinationUpdate = new PolicyDestinationUpdate(destination.Chats.ExceptBy(chats, ch => ch.Key).ToDictionary(k => k.Key, v => v.Value));

                update = BuildPolicyUpdate(policy, destinationUpdate, initiator);
            }

            return update is not null;
        }

        private static bool CanRemoveChatsFromPolicy(PolicyDestination destination, HashSet<Guid> chats) =>
            !destination.AllChats && destination.Chats.Any(pair => chats.Contains(pair.Key));

        private static PolicyUpdate BuildPolicyUpdate(Policy policy, PolicyDestinationUpdate destination, InitiatorInfo initiator) =>
            new()
            {
                Id = policy.Id,
                Conditions = policy.Conditions.Select(c => new
                PolicyConditionUpdate(c.Operation, c.Property, c.Target, c.Combination)).ToList(),
                ConfirmationPeriod = policy.ConfirmationPeriod,
                Status = policy.Status,
                Template = policy.Template,
                Icon = policy.Icon,
                IsDisabled = policy.IsDisabled,
                Destination = destination,
                Initiator = initiator,
            };


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
            model.Policies.ChangesHandler += _journalService.AddRecord;
            model.ChangesHandler += _journalService.AddRecord;
        }

        private void RemoveBaseNodeSubscription(BaseNodeModel model)
        {
            model.Settings.ChangesHandler -= _journalService.AddRecord;
            model.Policies.ChangesHandler -= _journalService.AddRecord;
            model.ChangesHandler -= _journalService.AddRecord;
        }

        private void SubscribeSensorToPolicyUpdate(BaseSensorModel sensor)
        {
            sensor.Policies.SensorExpired += SetExpiredSnapshot;
            sensor.Policies.Uploaded += UpdatePolicy;

            sensor.UpdateFromParentSettings += _database.UpdateSensor;
            sensor.ReadDataFromDb += GetSensorValues;

            AddBaseNodeSubscription(sensor);
        }

        private void RemoveSensorPolicies(BaseSensorModel sensor)
        {
            foreach (var policyId in sensor.Policies.Select(u => u.Id))
                sensor.Policies.RemovePolicy(policyId);

            sensor.Policies.SensorExpired -= SetExpiredSnapshot;
            sensor.Policies.Uploaded -= UpdatePolicy;

            sensor.UpdateFromParentSettings -= _database.UpdateSensor;
            sensor.ReadDataFromDb -= GetSensorValues;

            RemoveBaseNodeSubscription(sensor);
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
                sensor = AddSensor(storeInfo, value.Type, parentProduct, DefaultAlertsOptions.None);
            else if (sensor.State == SensorState.Blocked)
                return;

            var oldStatus = sensor.Status;

            if (sensor.TryAddValue(value) && sensor.LastDbValue != null)
                SaveSensorValueToDb(sensor.LastDbValue, sensor.Id);

            SensorUpdateViewAndNotify(sensor);
        }


        private void SaveSensorValueToDb(BaseValue value, Guid sensorId, bool ignoreSnapshot = false)
        {
            _database.AddSensorValue(value.ToEntity(sensorId));

            if (!value.IsTimeout && !ignoreSnapshot)
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

            ApplyProducts(RequestProducts());
            ApplySensors(RequestSensors(), RequestPolicies());

            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} is requesting");
            var accessKeysEntities = _database.GetAccessKeys();
            _logger.Info($"{nameof(IDatabaseCore.GetAccessKeys)} requested");

            _logger.Info($"Migrate sensors settings and alerts");
            RunMigration();
            _logger.Info($"Migrate sensor settings and alerts finished");

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys([.. accessKeysEntities]);
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            _logger.Info($"{nameof(TreeValuesCache)} initialized");

            UpdateCacheState();
        }

        private void RunMigration()
        {
            try
            {
                foreach (var update in MigrationManager.GetMigrationUpdates([.. _sensors.Values]))
                    TryUpdateSensor(update, out _);
            }
            catch (Exception ex)
            {
                _logger.Error($"Migration is failed: {ex}");
            }
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

            return policyEntities.ToDictionary(k => new Guid(k.Id).ToString(), v => v);
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

        private void ApplySensors(List<SensorEntity> sensorEntities, Dictionary<string, PolicyEntity> policies)
        {
            _logger.Info($"{nameof(sensorEntities)} are applying");
            BuildAndSubscribeSensors(sensorEntities, policies);
            _logger.Info($"{nameof(sensorEntities)} applied");

            _logger.Info("Links between products and their sensors are building");
            foreach (var sensorEntity in sensorEntities)
                if (!string.IsNullOrEmpty(sensorEntity.ProductId))
                {
                    var parentId = Guid.Parse(sensorEntity.ProductId);
                    var sensorId = Guid.Parse(sensorEntity.Id);

                    if (_tree.TryGetValue(parentId, out var parent) && _sensors.TryGetValue(sensorId, out var sensor))
                        parent.AddSensor(sensor);
                    else
                        RemoveSensor(sensorId);
                }
            _logger.Info("Links between products and their sensors are built");

            _logger.Info($"{nameof(FillSensorsData)} is started");
            FillSensorsData();
            _logger.Info($"{nameof(FillSensorsData)} is finished");
        }

        private void BuildAndSubscribeSensors(List<SensorEntity> entities, Dictionary<string, PolicyEntity> policies)
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

        private BaseSensorModel AddSensor(BaseRequestModel request, SensorType type, ProductModel parent, DefaultAlertsOptions options)
        {
            SensorEntity entity = new()
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = request.PathParts[^1],
                Type = (byte)type,
                CreationDate = DateTime.UtcNow.Ticks,
            };

            var sensor = SensorModelFactory.Build(entity);
            parent.AddSensor(sensor);

            if (!sensor.Settings.TTL.IsSet)
                sensor.Policies.TimeToLive.ApplyParent(parent.Policies.TimeToLive, options.HasFlag(DefaultAlertsOptions.DisableTtl));

            SubscribeSensorToPolicyUpdate(sensor);

            sensor.Policies.AddDefault(options);

            AddSensor(sensor);
            UpdateProduct(parent);

            return sensor;
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

                        SendNotification(sensor.Notifications.LeftOnlyScheduled());

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
            _confirmationManager.FlushMessages();
            _scheduleManager.FlushMessages();

            foreach (var sensor in GetSensors())
                CheckSensorTimeout(sensor);

            foreach (var key in GetAccessKeys())
                if (key.IsExpired && key.State < KeyState.Expired)
                    UpdateAccessKeyState(key.Id, KeyState.Expired);

            foreach (var sensor in GetSensors())
                if (sensor.EndOfMuting <= DateTime.UtcNow)
                    UpdateMutedSensorState(sensor.Id, InitiatorInfo.System);
        }

        void CheckSensorTimeout(BaseSensorModel sensor)
        {
            sensor.CheckTimeout();

            var ttl = sensor.Policies.TimeToLive;

            if (sensor.HasData && ttl.ResendNotification(sensor.LastValue.Time))
                SendNotification(ttl.PolicyResult);
        }

        private void SetExpiredSnapshot(BaseSensorModel sensor, bool timeout)
        {
            var snapshot = _snapshot.Sensors[sensor.Id];

            if (snapshot.IsExpired != timeout)
            {
                var ttl = sensor.Policies.TimeToLive;
                snapshot.IsExpired = timeout;

                if (timeout && sensor.HasData)
                {
                    var value = sensor.GetTimeoutValue();

                    if ((sensor.LastTimeout is null || sensor.LastTimeout.ReceivingTime < sensor.LastUpdate) && sensor.TryAddValue(value))
                        SaveSensorValueToDb(value, sensor.Id);
                }

                SendNotification(ttl.GetNotification(timeout));
            }

            SensorUpdateView(sensor);
        }
    }
}