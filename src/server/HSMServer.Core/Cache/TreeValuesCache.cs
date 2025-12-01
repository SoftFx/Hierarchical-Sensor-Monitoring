using AngleSharp.Dom;
using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDataCollector.DefaultSensors;
using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.ApiObjectsConverters;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Confirmation;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Threading;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.PathTemplates;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SensorType = HSMServer.Core.Model.SensorType;



namespace HSMServer.Core.Cache
{
    public sealed class TreeValuesCache : ITreeValuesCache, IUpdateHandler, IAsyncDisposable
    {
        private class CachedValue
        {
            public IUpdatesQueue Queue { get; }

            public ProductModel Product { get; }

            public ConcurrentDictionary<string, BaseSensorModel> Sensors { get; } = new ConcurrentDictionary<string, BaseSensorModel>(StringComparer.Ordinal);


            public CachedValue(ProductModel product, IUpdateHandler updateHandler)
            {
                Product = product;
                Queue = new UpdatesQueue(product.DisplayName, updateHandler.ProcessRequest);
            }
        }

        private const string NotInitializedCacheError = "Cache is not initialized yet.";
        private const string NotExistingSensor = "Sensor with your path does not exist.";
        private const string ErrorProductNotFound = "Product doesn't exist.";
        private const string ErrorKeyNotFound = "Key doesn't exist.";

        private const string ErrorMasterKey =
            "Master key is invalid for this request because product is not specified.";

        public const int MaxHistoryCount = 50000;

        private readonly static MigrationManager _migrator = new();

        private readonly ConcurrentDictionary<Guid, CachedValue> _cache = new();
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensorsById = new();
        private readonly ConcurrentDictionary<Guid, AccessKeyModel> _keys = new();
        private readonly ConcurrentDictionary<Guid, ProductModel> _tree = new();
        private readonly ConcurrentDictionary<Guid, AlertTemplateModel> _alertTemplates = new();
        private readonly ConcurrentDictionary<string, ProductModel> _productsByName = new(StringComparer.Ordinal);

        private readonly CDict<bool> _fileHistoryLocks = new(); // TODO: get file history should be fixed without this crutch

        private readonly Logger _logger = LogManager.GetLogger(nameof(TreeValuesCache));

        private readonly ConfirmationManager _confirmationManager = new();
        private readonly ScheduleManager _scheduleManager = new();

        private readonly Task _updateTask;
        private readonly CancellationTokenSource _cts = new();

        private readonly ITreeStateSnapshot _snapshot;
        private readonly IJournalService _journalService;
        private readonly IDatabaseCore _database;

        private readonly TimeSpan StateUpdatePeriod = TimeSpan.FromMinutes(1);
        private readonly TimeSpan StateUpdateStartDelay = TimeSpan.FromMinutes(2);

        public event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;
        public event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        public event Action<ProductModel, ActionType> ChangeProductEvent;

        public event Action<string, int, int> RequestProcessed;

        public event Action<AlertMessage> NewAlertMessageEvent;
        public event Action<FolderEventArgs> FillFolderChats;


        public int SensorsCount => _sensorsById.Count;


        public TreeValuesCache(IDatabaseCore database, ITreeStateSnapshot snapshot,
            IJournalService journalService)
        {
            _database = database;
            _snapshot = snapshot;

            _journalService = journalService;

            Initialize();

            _confirmationManager.NewMessageEvent += _scheduleManager.ProcessMessage;
            _scheduleManager.NewMessageEvent += SendAlertMessage;

            _updateTask = PeriodicTask.Run(UpdateCacheState, StateUpdateStartDelay, StateUpdatePeriod, _cts.Token, _logger);
        }


        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();

            if (_updateTask != null)
            {
                try
                {
                    await _updateTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.Warn("Update task timeout during disposal");
                }
            }

            foreach (var queue in _cache.Values)
                await queue.Queue.DisposeAsync();

            _cts?.Dispose();

            _confirmationManager.NewMessageEvent -= _scheduleManager.ProcessMessage;
            _scheduleManager.NewMessageEvent -= SendAlertMessage;

            _database.Dispose();
        }


        public List<ProductModel> GetAllNodes()
        {
            return [.. _tree.Values];
        }

        public List<BaseSensorModel> GetSensors()
        {
            return [.. _sensorsById.Values];
        }

        public List<BaseSensorModel> GetSensors(string wildcard, SensorType? sensorType = null, Guid? folderId = null)
        {
            return [.. GetSensorsByWildcard(wildcard, sensorType, folderId)];
        }

        private IEnumerable<BaseSensorModel> GetSensorsByWildcard(string wildcard, SensorType? sensorType = null, Guid? folderId = null, Guid? productId = null)
        {
            PathTemplateConverter converter = new PathTemplateConverter();
            if (!converter.ApplyNewTemplate(wildcard, out string errors))
                return [];

            var result = _sensorsById.Values.Where(x => converter.IsMatch(x.FullPath));

            if (folderId != null)
                result = result.Where(x => x.Root.FolderId == folderId);

            if (sensorType.HasValue)
                result = result.Where(x => x.Type == sensorType.Value);

            if (productId.HasValue)
                result = result.Where(x => x.Root.Id == productId.Value);

            return result;
        }

        public List<AccessKeyModel> GetAccessKeys()
        {
            return [.. _keys.Values];
        }

        public async Task<ProductModel> AddProductAsync(string productName, Guid authorId, CancellationToken token = default)
        {
            var request = new AddProductRequest(productName, authorId);

            if (!_cache.TryAdd(request.ProductModel.Id, new CachedValue(request.ProductModel, this)))
                throw new Exception($"Product {productName} already exists");

            await ProcessRequestAsync(request.ProductModel.Id, request, token);
            return request.ProductModel;

        }

        private void UpdateProduct(ProductModel product)
        {
            _database.UpdateProduct(product.ToEntity());
            ChangeProductEvent?.Invoke(product, ActionType.Update);
        }

        public bool TryGetProduct(Guid productId, out ProductModel product)
        {
            return _tree.TryGetValue(productId, out product);
        }

        public bool TryGetProduct(string productId, out ProductModel product)
        {
            product = null;

            if (string.IsNullOrWhiteSpace(productId))
                return false;

            if (Guid.TryParse(productId, out var id))
                return TryGetProduct(id, out product);

            return false;
        }

        public async Task UpdateProductAsync(ProductUpdate request, CancellationToken token)
        {
            var product = GetProduct(request.Id);

            if (product == null)
                return;

            if (!product.IsRoot)
                product = product.Root;

            await ProcessRequestAsync(product.Id, request, token);
        }


        private void UpdateProduct(ProductUpdate update)
        {
            if (!_tree.TryGetValue(update.Id, out ProductModel product))
                return;

            if (product.IsRoot && !string.IsNullOrEmpty(update.Name) && product.DisplayName != update.Name)
            {
                _productsByName.Remove(product.DisplayName, out _);
                _productsByName.TryAdd(update.Name, product);
            }

            _database.UpdateProduct(product.Update(update).ToEntity());

            NotifyAboutProductChange(product);
        }


        public async Task RemoveProductAsync(Guid productId, InitiatorInfo initiator = null, CancellationToken token = default)
        {
            var product = GetProduct(productId);
            if (product != null)
            {
                if (!product.IsRoot)
                    product = product.Root;

                await ProcessRequestAsync(product.Id, new RemoveProductRequest(productId, initiator), token);
            }
        }

        private void RemoveProduct(RemoveProductRequest request)
        {
            void RemoveProduct(Guid productId, InitiatorInfo initiator)
            {
                if (!_tree.Remove(productId, out var product))
                    return;

                foreach (var (subProductId, _) in product.SubProducts)
                    RemoveProduct(subProductId, initiator);

                foreach (var (sensorId, _) in product.Sensors)
                    RemoveSensor(sensorId, initiator, product.Parent?.Id);

                RemoveBaseNodeSubscription(product);

                _logger.Info($"Node removed: Id = {product.Id}, Name = {product.DisplayName}, Path = {product.Path}, RootId = {product.Root?.Id}, RootName = {product.RootProductName}, Created = {product.CreationDate}, Settings = {product.Settings.SelfDestroy}");

                product.Parent?.SubProducts.TryRemove(productId, out _);
                _database.RemoveProduct(product.Id.ToString());

                foreach (var (id, _) in product.AccessKeys)
                    RemoveAccessKey(id);

                ChangeProductEvent?.Invoke(product, ActionType.Delete);
            }


            try
            {
                if (TryGetProduct(request.Id, out var product))
                {
                    if (product.IsRoot)
                    {
                        _productsByName.TryRemove(product.DisplayName, out _);
                    }

                    RemoveProduct(request.Id, request.InitiatorInfo);

                    RemoveProductFromCache(product);

                    if (!product.IsRoot)
                        UpdateProduct(product.Parent);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        public async Task RunSensorsSelfDestroyAsync(CancellationToken token = default)
        {
            _logger.Info("Start sensors sefl destroy");

            var sensors = GetSensors();
            var removed = 0;
            foreach (var sensor in sensors)
            {
                if (token.IsCancellationRequested)
                    break;

                if (sensor.ShouldDestroy())
                {
                    await RemoveSensorAsync(sensor.Id, InitiatorInfo.AsSystemInfo("Clean up"), token: token);
                    removed++;
                }
            }

            _logger.Info($"Stop sensors self destroy: removed {removed} of {sensors.Count} ");
        }

        public async Task RunProductsSelfDestroyAsync(CancellationToken token = default)
        {
            _logger.Info("Start product self destroy");

            var products = GetProducts();

            var removed = 0;
            foreach (var product in products)
            {
                if (token.IsCancellationRequested)
                    break;

                await ClearEmptyNodesAsync(product, token);
                removed++;
            }

            _logger.Info($"Stop product self destroy: removed {removed} of {products.Count}");
        }


        public ProductModel GetProduct(Guid id)
        {
            return _tree.GetValueOrDefault(id);
        }

        public ProductModel GetProductByName(string name)
        {
            _productsByName.TryGetValue(name, out var product);
            return product;
        }

        public bool TryGetProductByName(string name, out ProductModel product)
        {
            return _productsByName.TryGetValue(name, out product);
        }

        public bool TryGetProductNameById(Guid id, out string name)
        {
            name = string.Empty;
            if (TryGetProduct(id, out var product))
            {
                name = product.DisplayName;
                return true;
            }

            return false;
        }

        /// <returns>list of root products (without parent)</returns>
        public List<ProductModel> GetProducts()
        {
            return [.. _productsByName.Values];
        }


        public bool TryCheckKeyWritePermissions(Guid key, string[] parts, out string message)
        {

            if (!TryCheckProductKey(key, out var product, out message))
                return false;

            var accessKey = GetAccessKeyModel(key);

            if (!accessKey.IsValid(KeyPermissions.CanSendSensorData, out message))
                return false;

            var sensorChecking = TryGetSensor(parts, product, accessKey, out var sensor, out message);

            if (sensor?.State == SensorState.Blocked)
            {
                message = $"Sensor {sensor.RootProductName}{sensor.Path} is blocked.";
                return false;
            }

            return sensorChecking;
        }

        public void SetLastKeyUsage(Guid key, string ip)
        {
            if (!TryGetKey(key, out var keyModel, out _))
                return;

            var usageTime = DateTime.UtcNow;

            keyModel.UpdateUsageInfo(ip, usageTime);
            _snapshot.Keys[key].Update(ip, usageTime);

            ChangeAccessKeyEvent?.Invoke(keyModel, ActionType.Update);
        }

        public bool TryGetKey(Guid id, out AccessKeyModel key, out string message)
        {

            key = _keys.TryGetValue(id, out var keyModel) ? keyModel : AccessKeyModel.InvalidKey;

            if (!key.IsValidState(out message))
                return false;

            if (!key.IsMaster)
                return true;

            message = ErrorMasterKey;
            return false;
        }

        public bool TryGetRootProduct(Guid id, out ProductModel product, out string error)
        {
            var ok = TryGetProduct(id, out product) && product.IsRoot;
            error = !ok ? ErrorProductNotFound : null;

            return ok;
        }

        //public bool TryCheckKeyReadPermissions(BaseUpdateRequest request, out string message) =>
        //    TryGetProductByKeyInternal(request, out var product, out message) &&
        //    GetAccessKeyModel(request).IsValid(KeyPermissions.CanReadSensorData, out message) &&
        //    TryGetSensor(request, product, null, out _, out message);

        //public bool TryCheckSensorUpdateKeyPermission(BaseUpdateRequest request, out Guid sensorId, out string message)
        //{
        //    sensorId = Guid.Empty;

        //    if (!TryCheckProductKey(request, out var product, out message))
        //        return false;

        //    var accessKey = GetAccessKeyModel(request);
        //    var sensorChecking = TryGetSensor(request, product, accessKey, out var sensor, out message);

        //    if (sensor is not null)
        //        sensorId = sensor.Id;

        //    return sensorChecking;
        //}

        private bool TryCheckProductKey(Guid accessKey, out ProductModel product, out string message)
        {
            if (!TryGetProductByKeyInternal(accessKey, out product, out message))
                return false;

            // TODO: remove after refactoring sensors data storing
            if (!product.IsRoot)
            {
                message = "Temporarily unavailable feature. Please select a product without a parent";
                return false;
            }

            return true;
        }


        public AccessKeyModel AddAccessKey(AccessKeyModel key)
        {
            try
            {
                if (TryAddKeyToTree(key))
                {
                    _database.AddAccessKey(key.ToAccessKeyEntity());

                    ChangeAccessKeyEvent?.Invoke(key, ActionType.Add);

                    return key;
                }
                else
                {
                    _logger.Error($"{nameof(AddAccessKey)} an error occurred: {key}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(nameof(AddAccessKey), ex);
                return null;
            }
        }

        public AccessKeyModel RemoveAccessKey(Guid id)
        {
            try
            {
                if (_keys.Remove(id, out var key))
                {
                    if (TryGetProduct(key.ProductId, out var product))
                    {
                        product.AccessKeys.TryRemove(id, out _);
                        ChangeProductEvent?.Invoke(product, ActionType.Update);
                    }

                    _database.RemoveAccessKey(id);
                    _snapshot.Keys.Remove(id);

                    ChangeAccessKeyEvent?.Invoke(key, ActionType.Delete);

                    return key;
                }
                else
                {
                    _logger.Error($"{nameof(RemoveAccessKey)} key not found: {id}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"{nameof(RemoveAccessKey)} an error occurred: {id}", ex);
                return null;
            }
        }

        public AccessKeyModel UpdateAccessKey(AccessKeyUpdate updatedKey)
        {
            try
            {
                if (_keys.TryGetValue(updatedKey.Id, out var key))
                {

                    key.Update(updatedKey);
                    _database.UpdateAccessKey(key.ToAccessKeyEntity());

                    ChangeAccessKeyEvent?.Invoke(key, ActionType.Update);

                    return key;
                }
                else
                {
                    _logger.Error($"{nameof(UpdateAccessKey)} key not found: {updatedKey}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(nameof(UpdateAccessKey), ex);
                return null;
            }
        }

        public AccessKeyModel UpdateAccessKeyState(Guid id, KeyState newState)
        {
           return !_keys.TryGetValue(id, out var key) ? null : UpdateAccessKey(new AccessKeyUpdate(key.Id, newState));
        }

        public AccessKeyModel GetAccessKey(Guid id)
        {
            return _keys.GetValueOrDefault(id);
        }

        public List<AccessKeyModel> GetMasterKeys()
        {
            return _keys.Values.Where(x => x.IsMaster).ToList();
        }

        public Task<TaskResult> AddOrUpdateSensorAsync(SensorAddOrUpdateRequest request, CancellationToken token = default)
        {
            return ProcessRequestAsync(request.ProductId, request, token);
        }

        private bool TryAddOrUpdateSensor(SensorAddOrUpdateRequest request, out string error)
        {
            var update = request.Update;

            if (!TryGetSensorFromCache(request.ProductId, request.Path, out var sensor))
                if (!TryAddSensor(request, request.Type, request.ProductId, request.Update.DefaultAlertsOptions, out sensor, out error))
                    return false;

            update = update with { Id = sensor.Id };
            return TryUpdateSensor(update, out error);
        }

        public async Task<TaskResult> UpdateSensorAsync(SensorUpdate update)
        {
            if (!TryGetSensorById(update.Id, out var sensor))
                return TaskResult.FromError($"Sensor {update.Id} was not found");

            return await ProcessRequestAsync(sensor.Root.Id, update);
        }


        private bool TryUpdateSensor(SensorUpdate update, out string error)
        {
            try
            {
                if (!TryGetSensorById(update.Id, out var sensor))
                {
                    error = $"Sensor doesn't exist {update.Id}";
                    return false;
                }

                sensor.TryUpdate(update, out error);
                _database.UpdateSensor(sensor.ToEntity());

                SensorUpdateView(sensor);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public async Task<TaskResult> UpdateSensorValueAsync(UpdateSensorValueRequestModel request, CancellationToken token = default)
        {
            if (!TryGetSensorById(request.Id, out var sensor))
                return TaskResult.FromError($"Sensor {sensor.Id} was not found"); 

            return await ProcessRequestAsync(sensor.Root.Id, request, token);
        }

        private void UpdateSensorValue(UpdateSensorValueRequestModel request)
        {
            try
            {
                if (TryGetSensorById(request.Id, out var sensor))
                {
                    var lastValue = sensor.LastValue;

                    if (request.Comment is not null && (!request.ChangeLast || lastValue is not null))
                    {
                        var value = request.BuildNewValue(sensor);
                        var result = request.ChangeLast ? sensor.TryUpdateLastValue(value) : sensor.TryAddValue(value);

                        if (result)
                        {
                            var (oldValue, newValue) =
                                request.GetValues(lastValue, sensor.LastValue); // value can be rebuild in storage so use LastValue

                            _journalService.AddRecord(new JournalRecordModel(request.Id, request.Initiator)
                            {
                                PropertyName = request.PropertyName,
                                Enviroment = request.Environment,
                                Path = sensor.FullPath,
                                OldValue = request.BuildComment(lastValue?.Status, lastValue?.Comment, oldValue),
                                NewValue = request.BuildComment(value: newValue)
                            });

                            if (sensor.LastDbValue != null)
                                SaveSensorValueToDb(sensor.LastDbValue, request.Id, false);

                            SensorUpdateViewAndNotify(sensor);
                        }
                    }
                }
                else
                {
                    _logger.Error($"Update Sensor error: [{request.Id}] sensor not found. {request}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Update Sensor error: {request}", ex);
            }
        }

        public async Task RemoveSensorAsync(Guid sensorId, InitiatorInfo initiator = null, Guid? parentId = null, CancellationToken token = default)
        {
            if (!TryGetSensorById(sensorId, out var sensor))
                return;

            var request = new RemoveSensorRequest(sensorId, initiator, parentId);

            await ProcessRequestAsync(sensor.Root.Id, request, token);
        }

        private void RemoveSensor(Guid sensorId, InitiatorInfo initiator = null, Guid? parentId = null)
        {
            try
            {
                if (!TryGetSensorById(sensorId, out var sensor))
                    return;

                RemoveSensorFromCache(sensor);

                RemoveSensorPolicies(sensor); // should be before removing from parent

                if (sensor.Parent is not null && (TryGetProduct(sensor.Parent.Id, out var parent) || parentId is not null))
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


                _logger.Info($"Sensor removed: Id = {sensor.Id}, Name = {sensor.DisplayName}, Path = {sensor.Path}, ProductId = {sensor.Root?.Id}, ProductName = {sensor.RootProductName}, LastUpdate = {sensor.LastUpdate}, Created = {sensor.CreationDate}, Settings = {sensor.Settings.SelfDestroy}, Initiator = {initiator}");

                _database.RemoveSensorWithMetadata(sensorId.ToString());

                ChangeSensorEvent?.Invoke(sensor, ActionType.Delete);
            }
            catch (Exception ex)
            {
                _logger.Error($"An error was occured by removing sensor {sensorId}", ex);
            }
        }

        public async Task UpdateMutedSensorStateAsync(Guid sensorId, InitiatorInfo initiator, DateTime? endOfMuting = null)
        {
            if (!TryGetSensorById(sensorId, out var sensor) || sensor.State is SensorState.Blocked)
                return;

            if (sensor.EndOfMuting != endOfMuting)
            {
                var update = new SensorUpdate
                {
                    Id = sensorId,
                    State = endOfMuting is null ? SensorState.Available : SensorState.Muted,
                    EndOfMutingPeriod = endOfMuting,
                    Initiator = initiator
                };

                await ProcessRequestAsync(sensor.Root.Id, update);
            }
        }

        public async Task ClearNodeHistoryAsync(ClearHistoryRequest request, CancellationToken token = default)
        {
            if (!TryGetProduct(request.Id, out var product))
                return;

            foreach (var (subProductId, _) in product.SubProducts)
                await ClearNodeHistoryAsync(request with {Id = subProductId}, token);

            foreach (var (sensorId, _) in product.Sensors)
                await ClearSensorHistoryAsync(request with {Id = sensorId}, token);
        }

        public async Task CheckSensorsHistoryAsync(CancellationToken token = default)
        {
            foreach (var product in GetProducts())
            {
                await ProcessRequestAsync(product.Id, new CheckSensorsHistoryRequest(product.Id), token);
            }
        }

        private void CheckSensorHistory(CheckSensorsHistoryRequest request)
        {
            _cache.TryGetValue(request.ProductId, out var value);

            _logger.Info($"Clear history started: [{value.Product.Id}] {value.Product.DisplayName}");

            int cleared = 0;
            foreach (var sensor in value.Sensors.Values)
            {
                var from = sensor.HistoryPeriod.From;
                var policy = sensor.Settings.KeepHistory.Value;

                if (policy.TimeIsUp(from))
                {
                    ClearSensorHistory(new(sensor.Id, policy.GetShiftedTime(DateTime.UtcNow, -1)));
                    cleared++;
                }
            }

            _logger.Info($"Clear history ended: [{value.Product.Id}] {value.Product.DisplayName} cleared {cleared} of {value.Sensors.Values.Count}");
        }

        public async Task ClearSensorHistoryAsync(ClearHistoryRequest request, CancellationToken token = default)
        {
            if (!TryGetSensorById(request.Id, out var sensor))
                return;

            await ProcessRequestAsync(sensor.Root.Id, request, token);
        }

        private void ClearSensorHistory(ClearHistoryRequest request)
        {
            if (!TryGetSensorById(request.Id, out var sensor))
            {
                _logger.Info($"{nameof(ClearSensorHistory)} {request.Id} failed: sensor not found");
                return;
            }

            var from = sensor.HistoryPeriod.From;
            var to = request.To;

            if (from > to)
            {
                _logger.Info($"{nameof(ClearSensorHistory)} {request.Id} failed: {from} > {to}");
                return;
            }

            sensor.Storage.Clear(to);

            if (!sensor.HasData)
                sensor.ResetSensor();

            if (sensor.AggregateValues)
            {
                if (IsBorderedValue(sensor, from.Ticks - 1, out var latestFrom) && from <= latestFrom.LastUpdateTime &&
                    latestFrom.LastUpdateTime <= to)
                    from = latestFrom.ReceivingTime;

                if (IsBorderedValue(sensor, to.Ticks, out var latestTo))
                    to = latestTo.ReceivingTime.AddTicks(-1);

                if (from > to)
                {
                    _logger.Info($"{nameof(ClearSensorHistory)} {request.Id} failed: {from} > {to}");
                    return;
                }
            }

            _database.ClearSensorValues(sensor.Id.ToString(), from, to);
            sensor.HistoryPeriod.From = to;

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


        public BaseSensorModel GetSensor(Guid sensorId)
        {
            if(!TryGetSensorById(sensorId, out var sensor))
                return null;

            return sensor;
        }


        public IEnumerable<BaseSensorModel> GetSensorsByFolder(HashSet<Guid> folderIds = null)
        {
            bool GetAnySensor(BaseSensorModel _) => true;
            bool GetSensorByFolder(BaseSensorModel sensor) => folderIds.Contains(sensor.Root.FolderId ?? Guid.Empty);

            Predicate<BaseSensorModel> filter = folderIds switch
            {
                null => GetAnySensor,
                _ => GetSensorByFolder,
            };

            return _sensorsById.Where(x => filter(x.Value)).Select(x => x.Value).ToList();
        }

        public bool TryGetSensorByPath(Guid productId, string path, out BaseSensorModel sensor)
        {
            if (!_cache.TryGetValue(productId, out var value))
            {
                sensor = null;
                return false;
            }

            return value.Sensors.TryGetValue(path, out sensor);
        }

        public void SendAlertMessage(AlertMessage message)
        {
            _logger.Info($"TSend: SendAlertMessage enter");
            
            if (message.IsEmpty)
                return;

            var sensorId = message.SensorId;

            if (!TryGetSensorById(sensorId, out var sensor) || !sensor.CanSendNotifications)
                return;

            var product = GetProductByName(sensor.RootProductName);

            if (product == null || !product.FolderId.HasValue)
                return;

            //TODO: move to Policy => GetParentChats when FolderModel will be moved into Core project
            List<Guid> folderChats = GetFolderChats(product.FolderId.Value);

            if (folderChats.Count != 0)
            {
                foreach (AlertResult alert in message)
                {
                    foreach (Guid folderId in folderChats)
                    {
                        alert.Destination.Chats.Add(folderId);
                    }
                }
            }

            _logger.Info($"TSend: NewAlertMessageEvent Invoke");
            NewAlertMessageEvent?.Invoke(message.ApplyFolder(product));
        }


        private List<Guid> GetFolderChats(Guid folderId)
        {
            FolderEventArgs args = new FolderEventArgs(folderId);
            FillFolderChats?.Invoke(args);

            if (!string.IsNullOrEmpty(args.Error))
                _logger.Error($"Loading folder temegrem chats error: {args.Error}");

            return args.ChatIDs;
        }


        private void SensorUpdateViewAndNotify(BaseSensorModel sensor)
        {
            SensorUpdateView(sensor);

            if (!sensor.Notifications.IsEmpty)
            {
                SendNotification(sensor.Id, sensor.Notifications);
            }
            else
            {
                if (!sensor.ConfirmationResult.IsEmpty)
                    _confirmationManager.UpdateNotifications(sensor.Id, sensor.ConfirmationResult);
            }
        }

        private void SendNotification(Guid sensorId, PolicyResult result) => _confirmationManager.RegisterNotification(sensorId, result);

        private void SensorUpdateView(BaseSensorModel sensor) => ChangeSensorEvent?.Invoke(sensor, ActionType.Update);


        public IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request)
        {
            if (!TryGetProductByKeyInternal(request.Key, out var product, out _) ||
                !TryGetSensor(request.PathParts, product, null, out var sensor, out _))
                return null;

            if (sensor is null)
                return null;

            var count = request.Count switch
                {
                    > 0 => Math.Min(request.Count.Value, MaxHistoryCount),
                    < 0 => Math.Max(request.Count.Value, -MaxHistoryCount),
                    _ => MaxHistoryCount
                };

            return count > 0
                ? GetSensorValuesPageInternal(sensor, request.From, request.To ?? DateTime.UtcNow.AddDays(1), count,
                    request.Options)
                : GetSensorValuesPageInternal(sensor, DateTime.MinValue, request.From, count, request.Options);
        }

        private ValueTask<List<BaseValue>> GetSensorValues(Guid sensorId, SensorHistoryRequest request) =>
            GetSensorValuesPage(sensorId, request.From, request.To, request.Count, request.Options).Flatten();

        public IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count, RequestOptions options = default)
        {

            var sensor = GetSensor(sensorId);
            return GetSensorValuesPageInternal(sensor, from, to, count, options);
        }

        private async IAsyncEnumerable<List<BaseValue>> GetSensorValuesPageInternal(BaseSensorModel sensor, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            bool IsNotTimout(BaseValue value) => !value.IsTimeout;

            if (sensor is FileSensorModel && _fileHistoryLocks[sensor.Id])
                yield return new List<BaseValue>();
            else
            {
                if (sensor is FileSensorModel)
                    _fileHistoryLocks[sensor.Id] = true;

                var includeTtl = options.HasFlag(RequestOptions.IncludeTtl);

                if (sensor.AggregateValues && IsBorderedValue(sensor, from.Ticks - 1, out var latest) &&
                    (includeTtl || IsNotTimout(latest)))
                    from = latest.ReceivingTime;

                var result = new List<BaseValue>(_database.SensorValuesPageCount);
                var totalCount = 0;
                var requestedCount = Math.Abs(count);
                
                await foreach (var byteValue in _database.GetSensorValues(sensor.Id, from, to))
                {
                    var convertedValue = sensor.Convert(byteValue);
                    if(!includeTtl && convertedValue.IsTimeout)
                        continue;

                    result.Add(convertedValue);
                    totalCount++;

                    if (result.Count == _database.SensorValuesPageCount)
                    {
                        yield return result.ToList();
                        result.Clear();
                    }

                    if (requestedCount == totalCount)
                    {
                        yield return result.ToList();
                        yield break;
                    }
                }

                yield return result.ToList();


                if (sensor is FileSensorModel)
                    _fileHistoryLocks[sensor.Id] = false;
            }
        }



        private async IAsyncEnumerable<List<BaseValue>> GetSensorValuesPageInternalOld(BaseSensorModel sensor, DateTime from, DateTime to, int count, RequestOptions options = default)
        {
            bool IsNotTimout(BaseValue value) => !value.IsTimeout;

            if (sensor is FileSensorModel && _fileHistoryLocks[sensor.Id])
                yield return new List<BaseValue>();
            else
            {
                if (sensor is FileSensorModel)
                    _fileHistoryLocks[sensor.Id] = true;

                var includeTtl = options.HasFlag(RequestOptions.IncludeTtl);

                if (sensor.AggregateValues && IsBorderedValue(sensor, from.Ticks - 1, out var latest) &&
                    (includeTtl || IsNotTimout(latest)))
                    from = latest.ReceivingTime;

                await foreach (var page in _database.GetSensorValuesPage(sensor.Id, from, to, count))
                {
                    var convertedValues = sensor.Convert(page);

                    yield return (includeTtl ? convertedValues : convertedValues.Where(IsNotTimout)).ToList();
                }


                if (sensor is FileSensorModel)
                    _fileHistoryLocks[sensor.Id] = false;
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

            if (TryGetProduct(nodeId, out var nodeModel))
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


        public AlertTemplateModel GetAlertTemplate(Guid id)
        {
            try
            {
                if (!_alertTemplates.TryGetValue(id, out AlertTemplateModel result))
                    return null;

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"An error was occurred while getting alert template with id = {id}", ex);
                return null;
            }
        }


        private void AddAlertFromTemplate(BaseSensorModel sensor, AlertTemplateModel alertTemplateModel)
        {
            PolicyUpdate ttlPolicyUpdate = null;
            List<PolicyUpdate> policyUpdates = [];
            TimeIntervalModel ttl = null;

            if (alertTemplateModel.TTLPolicy is not null)
            {
                ttlPolicyUpdate = new PolicyUpdate(alertTemplateModel.TTLPolicy, InitiatorInfo.AlertTemplate) { TemplateId = alertTemplateModel.Id };
                ttl = alertTemplateModel.TTL;
            }

            foreach (var policy in alertTemplateModel.Policies)
                policyUpdates.Add(new PolicyUpdate(policy, InitiatorInfo.AlertTemplate) { TemplateId = alertTemplateModel.Id });

            if (ttlPolicyUpdate != null || policyUpdates.Count > 0)
            {

                var update = new SensorUpdate()
                {
                    Id = sensor.Id,
                    Policies = policyUpdates,
                    TTLPolicy = ttlPolicyUpdate,
                    TTL = ttl,
                    Initiator = InitiatorInfo.AlertTemplate
                };

                TryUpdateSensor(update, out var error);
            }
        }

        public async Task SaveLastStateToDbAsync(CancellationToken token = default)
        {
            var products = GetProducts();

            await Parallel.ForEachAsync(products, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            }, async (product, ct) =>
            {
                await ProcessRequestAsync(product.Id, new SaveLastStateRequest(product.Id), ct);
            });
        }


        private void SaveLastStateToDb(SaveLastStateRequest request)
        {
            if (_cache.TryGetValue(request.ProductId, out var value))
            {
                foreach (var sensor in _sensorsById.Values)
                    if (sensor is IBarSensor barModel && barModel.LocalLastValue != default)
                        SaveSensorValueToDb(barModel.LocalLastValue, sensor.Id);
            }
        }


        public async Task AddAlertTemplateAsync(AlertTemplateModel alertTemplateModel, CancellationToken token = default)
        {
            var products = GetProducts().Where(x => x.FolderId == alertTemplateModel.FolderId).ToList();

            var first = products.FirstOrDefault();

            await Parallel.ForEachAsync(products, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            }, async (product, ct) =>
            {
                var isPrimary = product == first;
                var request = new AddAlertTemplateRequest(alertTemplateModel, product.Id, isPrimary);

                await ProcessRequestAsync(product.Id, request, ct);
            });
        }

        private void AddAlertTemplate(AddAlertTemplateRequest request)
        {
            var alertTemplateModel = request.AlertTemplateModel;

            try
            {
                if (_alertTemplates.ContainsKey(alertTemplateModel.Id))
                    RemoveAlertTemplate(new RemoveAlertTemplateRequest(alertTemplateModel.Id, request.ProductId, request.IsPrimary));


                foreach (var sensor in GetSensorsByWildcard(alertTemplateModel.Path, alertTemplateModel.GetSensorType(), alertTemplateModel.FolderId, request.ProductId).ToList())
                    AddAlertFromTemplate(sensor, alertTemplateModel);

                if (request.IsPrimary)
                {
                    _alertTemplates.GetOrAdd(alertTemplateModel.Id, () => alertTemplateModel);
                    _database.AddAlertTemplate(alertTemplateModel.ToEntity());
                }

            }
            catch (Exception ex)
            {
                _logger.Error($"An error was occurred while adding alert template {alertTemplateModel}", ex);
            }
        }


        public List<AlertTemplateModel> GetAlertTemplateModels()
        {
            return [.. _alertTemplates.Values];
        }

        public async Task RemoveAlertTemplateAsync(Guid id, CancellationToken token = default)
        {
            var templateModel = GetAlertTemplate(id);

            if (templateModel == null)
                return;

            var products = GetProducts().Where(x => x.FolderId == templateModel.FolderId);


            var first = products.FirstOrDefault();

            await Parallel.ForEachAsync(products, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            }, async (product, ct) =>
            {
                var isPrimary = product == first;
                var request = new RemoveAlertTemplateRequest(id, product.Id, isPrimary);

                await ProcessRequestAsync(product.Id, request, ct);
            });
        }


        private void RemoveAlertTemplate(RemoveAlertTemplateRequest request)
        {
            var product = GetProduct(request.ProductId);

            if (product == null)
                return;

            foreach (var sensor in product.GetAllSensors())
                if (sensor.Policies.TimeToLive.TemplateId == request.Id || sensor.Policies.Any(x => x.TemplateId == request.Id))
                {
                    foreach (var policy in sensor.Policies.Where(x => x.TemplateId == request.Id))
                        sensor.Policies.RemovePolicy(policy.Id, InitiatorInfo.AlertTemplate);

                    if (sensor.Policies.TimeToLive.TemplateId == request.Id)
                        sensor.Policies.UpdateTTL(new PolicyUpdate() { Initiator = InitiatorInfo.AlertTemplate });

                    _database.UpdateSensor(sensor.ToEntity());

                    sensor.Revalidate();

                    SensorUpdateView(sensor);
                }

            if (request.IsPrimary)
            {
                _alertTemplates.TryRemove(request.Id, out _);
                _database.RemoveAlertTemplate(request.Id);
            }
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
                            policyUpdate = BuildPolicyUpdate(policy, new(policy.Destination), initiator);

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

        private static bool TryGetPolicyUpdate(Policy policy, HashSet<Guid> chats, InitiatorInfo initiator,
            out PolicyUpdate update)
        {
            update = null;

            var destination = policy.Destination;
            if (CanRemoveChatsFromPolicy(destination, chats))
            {
                var destinationUpdate = new PolicyDestinationUpdate(destination.Chats.ExceptBy(chats, ch => ch.Key)
                    .ToDictionary(k => k.Key, v => v.Value));

                update = BuildPolicyUpdate(policy, destinationUpdate, initiator);
            }

            return update is not null;
        }

        private static bool CanRemoveChatsFromPolicy(PolicyDestination destination, HashSet<Guid> chats) =>
            destination.IsCustom && destination.Chats.Any(pair => chats.Contains(pair.Key));

        private static PolicyUpdate BuildPolicyUpdate(Policy policy, PolicyDestinationUpdate destination,
            InitiatorInfo initiator) =>
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


        private bool TryGetSensorById(Guid id, out BaseSensorModel sensor)
        {
            return _sensorsById.TryGetValue(id, out sensor);
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


        public async Task<TaskResult> AddSensorValueAsync(Guid key, Guid productId, SensorValueBase value, CancellationToken token = default)
        {
            var request = new AddSensorValueRequest(key, productId, value.Path, value.Convert());

            if (!request.TryCheckRequest(out var message))
                return TaskResult.FromError(message);

            if (!TryCheckKeyWritePermissions(request.Key, request.PathParts, out message))
                return TaskResult.FromError(message);

            await ProcessRequestAsync(productId, request, token);

            return TaskResult.Ok;
        }


        public async Task<Dictionary<string, string>> AddSensorValuesAsync(Guid key, Guid productId, IEnumerable<SensorValueBase> values, CancellationToken token = default)
        {
            var request = new AddSensorValuesRequest(key, productId, values);

            await ProcessRequestAsync(productId, request, token);

            return request.Response;
        }


        void IUpdateHandler.ProcessRequest(IUpdatesQueue queue, IUpdateRequest item)
        {
            queue.Stopwatch.Restart();

            switch (item)
            {
                case AddSensorValueRequest request when !TryAddNewSensorValue(request, out var error):
                    _logger.Error(error);
                    break;
                case AddSensorValuesRequest request:
                    AddNewSensorValues(request);
                    break;
                case SensorAddOrUpdateRequest command when !TryAddOrUpdateSensor(command, out var error):
                    _logger.Error(error);
                    break;
                case SensorUpdate update when !TryUpdateSensor(update, out var error):
                    _logger.Error(error);
                    break;
                case UpdateSensorValueRequestModel request:
                    UpdateSensorValue(request);
                    break;
                case ExpireSensorsRequest request:
                    CheckSensorsTimeout(request);
                    break;
                case RemoveSensorRequest request:
                    RemoveSensor(request.SensorId, request.InitiatoInfo, request.ParentId);
                    break;
                case AddAlertTemplateRequest request:
                    AddAlertTemplate(request);
                    break;
                case RemoveAlertTemplateRequest request:
                    RemoveAlertTemplate(request);
                    break;
                case ClearHistoryRequest request:
                    ClearSensorHistory(request);
                    break;
                case AddProductRequest request:
                    AddProduct(request.ProductModel);
                    break;
                case ProductUpdate request:
                    UpdateProduct(request);
                    break;
                case RemoveProductRequest request:
                    RemoveProduct(request);
                    break;
                case SaveLastStateRequest request:
                    SaveLastStateToDb(request);
                    break;
                case CheckSensorsHistoryRequest request:
                    CheckSensorHistory(request);
                    break;
            }

            queue.Stopwatch.Stop();
            int time = (int)queue.Stopwatch.ElapsedMilliseconds;
            RequestProcessed?.Invoke(queue.Name, queue.QueueSize, time);

            if (time > 300)
                _logger.Warn($"Longtime processing request occurred: {time} ms, {item}");
        }

        private void AddNewSensorValues(AddSensorValuesRequest request)
        {
            foreach (var value in request.Values)
            {
                var addRequest = new AddSensorValueRequest(request.Key, request.ProductId, value.Path, value.Convert()) { Key = request.Key};

                if (!addRequest.TryCheckRequest(out var message))
                {
                    request.Response[addRequest.Path] = message;
                    continue;
                }

                if (!TryCheckKeyWritePermissions(addRequest.Key, addRequest.PathParts, out message))
                {
                    request.Response[addRequest.Path] = message;
                    continue;
                }

                if (!TryAddNewSensorValue(addRequest, out var error))
                {
                    request.Response[addRequest.Path] = error;
                }
            }
        }

        private bool TryAddNewSensorValue(AddSensorValueRequest request, out string error)
        {
            error = null;

            if(!TryGetSensorFromCache(request.ProductId, request.Path, out BaseSensorModel sensor))
            {
                _logger.Info($"Creating new sensor: ProductId = {request.ProductId}, Name = {request.SensorName}, Path = {request.Path}, CurrentNumber of sensors in cache = {_sensorsById.Count}");
                if (!TryAddSensor(request, request.BaseValue.Type, request.ProductId, DefaultAlertsOptions.None, out sensor, out error))
                    return false;
            }

            if (sensor.State == SensorState.Blocked)
                return true;

            if (sensor.TryAddValue(request.BaseValue) && sensor.LastDbValue != null)
                SaveSensorValueToDb(sensor.LastDbValue, sensor.Id);

            SensorUpdateViewAndNotify(sensor);

            return true;
        }


        private void SaveSensorValueToDb(BaseValue value, Guid sensorId, bool ignoreSnapshot = false)
        {
            _database.AddSensorValue(value.ToEntity(sensorId));
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

            _logger.Info($"{nameof(accessKeysEntities)} are applying");
            ApplyAccessKeys([.. accessKeysEntities]);
            _logger.Info($"{nameof(accessKeysEntities)} applied");

            _logger.Info($"{nameof(IDatabaseCore.GetAllAlertTemplates)} is requesting");
            var alertTemlatesEntities = _database.GetAllAlertTemplates();
            _logger.Info($"{nameof(IDatabaseCore.GetAllAlertTemplates)} requested");

            _logger.Info($"{nameof(alertTemlatesEntities)} are applying");
            foreach (var template in alertTemlatesEntities)
            {
                var model = new AlertTemplateModel(template);
                if (!_alertTemplates.ContainsKey(new Guid(template.Id)))
                    _alertTemplates.TryAdd(model.Id, model);
                else
                    _database.RemoveAlertTemplate(new Guid(template.Id));
            }
            _logger.Info($"{nameof(alertTemlatesEntities)} applied");

            _logger.Info($"{nameof(TreeValuesCache)} initialized");


            //TimeoutValueAfterRestartFix();
        }

        //private void TimeoutValueAfterRestartFix()
        //{
        //    foreach (var sensor in _sensorsById.Values)
        //    {
        //        if (_snapshot.Sensors.TryGetValue(sensor.Id, out var state) && state.IsExpired)
        //        {
        //            var lastValue = sensor.Convert(_database.GetLatestValue(sensor.Id, DateTime.UtcNow.Ticks));
        //            if ((!lastValue?.IsTimeout ?? false) && sensor.LastValue is not null)
        //            {
        //                var timeoutValue = sensor.GetTimeoutValue();

        //                SaveSensorValueToDb(timeoutValue, sensor.Id);
        //            }
        //        }
        //    }
        //}

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

            var result = new Dictionary<string, PolicyEntity>();

            foreach (var policyEntity in policyEntities)
            {
                var key = new Guid(policyEntity.Id).ToString();
                if (!result.ContainsKey(key))
                    result.Add(key, policyEntity);
                else
                    _logger.Error($"Duplicate policy id found {key}");
            }

            return result;
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

            _logger.Info($"{nameof(productEntities)} applied [{_tree.Count}]");

            _logger.Info("Links between products are building");

            foreach (var productEntity in productEntities)
            {
                if (!string.IsNullOrEmpty(productEntity.ParentProductId))
                {
                    var parentId = Guid.Parse(productEntity.ParentProductId);
                    var productId = Guid.Parse(productEntity.Id);

                    if (TryGetProduct(parentId, out var parent) && TryGetProduct(productId, out var product))
                    {
                        parent.AddSubProduct(product);
                    }
                    else
                    {
                        _logger.Info($"Removing node id={productId}, parentId={parentId}," +
                                     $" parentExists={_tree.ContainsKey(productId)}," +
                                     $" REMOVE WILL BE APPLIED");

                        _database.RemoveProduct(productId.ToString());
                        _tree.Remove(productId, out var deletedProduct);

                        ChangeProductEvent?.Invoke(deletedProduct, ActionType.Delete);
                    }
                }
            }

            _logger.Info("Links between products are built");

            foreach (var product in _tree.Values.Where(x => x.IsRoot))
                _productsByName.TryAdd(product.DisplayName, product);

            _logger.Info($"Produts cache by name initialized [{_productsByName.Count}]");
        }

        private void ApplySensors(List<SensorEntity> sensorEntities, Dictionary<string, PolicyEntity> policies)
        {
            _logger.Info("Sensors are applying");
            foreach (var entity in sensorEntities)
            {
                try
                {
                    var sensor = SensorModelFactory.Build(entity);
                    sensor.Policies.ApplyPolicies(entity.Policies, policies);

                    var productId = Guid.Parse(entity.ProductId);
                    if (TryGetProduct(productId, out var parent))
                    {
                        parent.AddSensor(sensor);

                        var product = parent.Root;

                        AddSensorToCache(product, sensor);

                        SubscribeSensorToPolicyUpdate(sensor);
                        SensorUpdateView(sensor);
                    }
                    else
                    {
                        _logger.Info($"Removing sensor id={sensor.Id}, parentId={productId}," +
                                     $" Name = {sensor.DisplayName}," +
                                     $" parentExists={_tree.ContainsKey(productId)}," +
                                     $" REMOVE WILL BE APPLIED");

                        _database.RemoveSensorWithMetadata(entity.Id);

                        ChangeSensorEvent?.Invoke(sensor, ActionType.Delete);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Applying sensor {entity.Id} error: {ex.Message}");
                }
            }

            _logger.Info("Sensors are applied");

            _logger.Info($"{nameof(FillSensorsData)} is started");
            FillSensorsData();
            _logger.Info($"{nameof(FillSensorsData)} is finished");

            _logger.Info($"Set initial sensor state is started");
            foreach (var sensor in _sensorsById.Values)
                sensor.Policies.TimeToLive.InitLastTtlTime(sensor.CheckTimeout());
            _logger.Info($"Set initial sensor state is finished");
        }

        private void ApplyAccessKeys(List<AccessKeyEntity> entities)
        {
            foreach (var keyEntity in entities)
                TryAddKeyToTree(new AccessKeyModel(keyEntity));

            foreach (var product in _tree.Values)
            {
                if (product.AccessKeys.IsEmpty)
                    AddAccessKey(AccessKeyModel.BuildDefault(product));
            }
        }

        private ProductModel AddNonExistingProductsAndGetParentProduct(ProductModel parentProduct, BaseUpdateRequest request)
        {
            var pathParts = request.PathParts;
            var authorId =  parentProduct.AccessKeys.Values.FirstOrDefault().AuthorId;

            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                var subProductName = pathParts[i];
                var subProduct = parentProduct.SubProducts
                    .FirstOrDefault(p => p.Value.DisplayName == subProductName).Value;
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
                if (product.IsRoot)
                {
                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        TTL = new TimeIntervalModel(TimeInterval.None),
                        KeepHistory = new TimeIntervalModel(TimeInterval.Month),
                        SelfDestroy = new TimeIntervalModel(TimeInterval.Month),

                        DefaultChats = new PolicyDestinationSettings(DefaultChatsMode.FromParent),
                    };

                    product.Update(update);

                    _productsByName.TryAdd(product.DisplayName, product);
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

        private bool TryAddSensor(BaseUpdateRequest request, SensorType type, Guid productId, DefaultAlertsOptions options, out BaseSensorModel sensor, out string error)
        {
            sensor = null;
            error = string.Empty;
            try
            {

                if (!TryGetProduct(productId, out var product))
                {
                    error = $"Can't find product {productId}.";
                    return false;
                }

                var parentProduct = AddNonExistingProductsAndGetParentProduct(product, request);

                var sensorName = request.SensorName;

                SensorEntity entity = new()
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = request.SensorName,
                    Type = (byte) type,
                    DisplayUnit = 0,
                    CreationDate = DateTime.UtcNow.Ticks,
                };

                sensor = SensorModelFactory.Build(entity);
                parentProduct.AddSensor(sensor);

                if (!sensor.Settings.TTL.IsSet)
                    sensor.Policies.TimeToLive.ApplyParent(parentProduct.Policies.TimeToLive,
                        options.HasFlag(DefaultAlertsOptions.DisableTtl));

                SubscribeSensorToPolicyUpdate(sensor);

                //sensor.Policies.AddDefault(options);

                AddSensor(sensor, product);
                UpdateProduct(parentProduct);

                _journalService.AddRecord(new JournalRecordModel(sensor.Id, InitiatorInfo.System)
                {
                    PropertyName = "sensor",
                    NewValue = sensor.FullPath
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("An error was occurred by creating sensor", ex);
                error = "Can't create sensor";
                return false;
            }
        }

        private void AddSensor(BaseSensorModel sensor, ProductModel productModel)
        {
            try
            {
                foreach (var template in _alertTemplates.Values)
                {
                    if (template.IsMatch(sensor))
                        AddAlertFromTemplate(sensor, template);
                }

                AddSensorToCache(productModel, sensor);

                _database.AddSensor(sensor.ToEntity());

                ChangeSensorEvent?.Invoke(sensor, ActionType.Add);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private bool TryAddKeyToTree(AccessKeyModel key)
        {
            bool isSuccess = _keys.TryAdd(key.Id, key);

            if (isSuccess && TryGetProduct(key.ProductId, out var product))
            {
                if (_snapshot.Keys.TryGetValue(key.Id, out var snapKey))
                    key.UpdateUsageInfo(snapKey.IP, snapKey.LastUseTime);

                isSuccess &= product.AccessKeys.TryAdd(key.Id, key);
                ChangeProductEvent?.Invoke(product, ActionType.Update);
            }

            return isSuccess;
        }

        private bool TryGetProductByKeyInternal(Guid accessKey, out ProductModel product, out string message)
        {
            product = null;

            if (!_keys.TryGetValue(accessKey, out var keyModel))
            {
                message = $"Access key {accessKey} not found";
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

        private static bool TryGetSensor(string[] parts, ProductModel product,
            AccessKeyModel accessKey, out BaseSensorModel sensor, out string message)
        {
            message = string.Empty;
            sensor = null;

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

        private AccessKeyModel GetAccessKeyModel(Guid key)
        {
            return _keys.TryGetValue(key, out var keyModel) ? keyModel : AccessKeyModel.InvalidKey;
        }

        private void FillSensorsData()
        {
            var results = _database.GetLastAndFirstValues(_sensorsById.Keys);

            foreach (var (sensorId, (firstValueBytes, lastValueBytes)) in results)
            {
                if (lastValueBytes is not null && TryGetSensorById(sensorId, out var sensor))
                {
                    var lastValue = sensor.AddDbValue(lastValueBytes);

                    if (lastValue.IsTimeout)
                        sensor.IsExpired = true;

                    var firstValue = sensor.Convert(firstValueBytes);

                    sensor.HistoryPeriod.From = firstValue.Time;
                    sensor.HistoryPeriod.To = lastValue.Time;

                    //SendNotification(sensor.Id, sensor.Notifications.LeftOnlyScheduled());

                    //if (!_snapshot.IsFinal && sensor.LastValue is not null)
                    //    _snapshot.Sensors[sensorId].SetLastUpdate(sensor.LastValue.ReceivingTime, sensor.CheckTimeout());
                }
            }
        }

        private async Task UpdateCacheState()
        {
            _logger.Info("Update cache state");

            _confirmationManager.FlushMessages();
            _scheduleManager.FlushMessages();

            await Parallel.ForEachAsync(GetProducts(), new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            }, async (product, ct) =>
            {
                var request = new ExpireSensorsRequest(product.Id);

                await ProcessRequestAsync(product.Id, request, ct);
            });


            foreach (var sensor in _sensorsById.Values)
            {
                if (sensor.EndOfMuting <= DateTime.UtcNow)
                    await UpdateMutedSensorStateAsync(sensor.Id, InitiatorInfo.System);
            }

            foreach (var key in _keys.Values)
                if (key.IsExpired && key.State < KeyState.Expired)
                    UpdateAccessKeyState(key.Id, KeyState.Expired);
        }

        public async Task ClearEmptyNodesAsync(ProductModel product, CancellationToken token = default)
        {
            foreach (var (_, node) in product.SubProducts)
                await ClearEmptyNodesAsync(node, token);

            if (!product.IsRoot && product.IsEmpty && product.Settings.SelfDestroy.Value.GetShiftedTime(product.CreationDate) < DateTime.UtcNow)
            {
                await RemoveProductAsync(product.Id, InitiatorInfo.AsSystemForce("Old empty node"), token);
            }
        }


        private void CheckSensorsTimeout(ExpireSensorsRequest request)
        {
            var product = GetProduct(request.ProductId);

            if (product == null)
                return;

            foreach (var sensor in product.GetAllSensors())
            {
                var timeout = sensor.CheckTimeout();
                var ttl = sensor.Policies.TimeToLive;

                if (sensor.HasData && ttl.ResendNotification(sensor.LastValue.LastUpdateTime))
                    SendNotification(sensor.Id, ttl.GetNotification(true));
            }
        }

        private void SetExpiredSnapshot(BaseSensorModel sensor, bool timeout)
        {
            if (sensor.IsExpired != timeout)
            {
                var ttl = sensor.Policies.TimeToLive;
                sensor.IsExpired = timeout;

                if (timeout && sensor.HasData)
                {
                    var value = sensor.GetTimeoutValue();

                    if ((sensor.LastTimeout is null || sensor.LastTimeout.ReceivingTime < sensor.LastUpdate) &&
                        sensor.TryAddValue(value))
                        SaveSensorValueToDb(value, sensor.Id);
                }

                SendNotification(sensor.Id, ttl.GetNotification(timeout));
            }

            SensorUpdateView(sensor);
        }



        private async Task<TaskResult> ProcessRequestAsync(Guid productId, IUpdateRequest request, CancellationToken token = default)
        {
            try
            {
                var value = _cache.GetOrAdd(productId, () =>
                            {
                                var product = GetProduct(productId);
                                return new CachedValue(product, this);
                            });

                return await value.Queue.ProcessRequestAsync(request, token);
            }
            catch (Exception ex)
            {
                var msg = $"An error was occurred while processing request: {request}";
                _logger.Error(msg, ex);
                return TaskResult.FromError(msg);
            }
        }


        private void AddSensorToCache(ProductModel product, BaseSensorModel sensor)
        {
            _cache.AddOrUpdate(product.Id,
                    key => 
                    { 
                        var value = new CachedValue(product, this);
                        value.Sensors.TryAdd(sensor.Path, sensor);
                        return value;
                    },
                    (key, existingValue) => 
                    {
                        existingValue.Sensors.TryAdd(sensor.Path, sensor);
                        return existingValue;
                    }
                    );

            _sensorsById.TryAdd(sensor.Id, sensor);
        }

        public bool TryGetSensorFromCache(Guid productId, string path, out BaseSensorModel model)
        {
            model = null;

            if (!_cache.TryGetValue(productId, out CachedValue cachedValue))
                return false;

            return cachedValue.Sensors.TryGetValue(path, out model);
        }

        private void RemoveSensorFromCache(BaseSensorModel sensor)
        {
            if (_cache.TryGetValue(sensor.Root.Id, out var value))
            {
                value.Sensors.TryRemove(sensor.Path, out _);
            }

            _sensorsById.TryRemove(sensor.Id, out _);
        }

        private void RemoveProductFromCache(ProductModel product)
        {
            _cache.TryRemove(product.Id, out var value);
        }

    }
}