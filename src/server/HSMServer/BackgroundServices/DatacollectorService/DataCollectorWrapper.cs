using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Authentication;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;
using HSMServer.Notifications;
using HSMSensorDataObjects.SensorRequests;


namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDataSender, IDisposable
    {
        private const string SelfCollectorName = "Self monitoring";
        public const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _collector;

        private readonly ITreeValuesCache _cache;

        // For the token-usage eviction sweep (#1403 review, round 2).
        private readonly IApiTokenManager _apiTokens;

        // The owner-existence half of the sweep's composed liveness
        // predicate (#1403 review, round 6).
        private readonly IUserManager _users;

        private readonly ProductModel _productModel;

        private readonly NotificationsCenter _notificationsCenter;

        private readonly Guid _key;
        private readonly Logger _logger;

        private DateTime? _lastUpdateDbSize = null;

        internal readonly TimeSpan DbSizeUpdateInterval = TimeSpan.FromDays(1);

        internal DatabaseSensorsStatistics DbStatisticsSensors { get; }

        internal ClientStatisticsSensors WebRequestsSensors { get; }

        // Per-token usage monitoring of the management API (#1402): request
        // rate + duration per (owner login, token EntityId), REST and MCP.
        internal ApiTokenUsageSensors ApiTokenUsageSensors { get; }

        internal DatabaseSensorsSize DbSizeSensors { get; }

        internal BackupSensors BackupSensors { get; }

        internal TreeValueChacheStatistics TreeValueCacheStatistics { get; } 

        internal TelegramBotStatistics TelegramBotStatistics { get; }

        internal SlackChannelStatistics SlackChannelStatistics { get; }

        internal MattermostChannelStatistics MattermostChannelStatistics { get; }


        public DataCollectorWrapper(ITreeValuesCache cache, IDatabaseCore db, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor, NotificationsCenter notificationCenter, IApiTokenManager apiTokens, IUserManager users)
        {
            _logger = LogManager.GetLogger(GetType().Name);

            _cache = cache;
            _apiTokens = apiTokens;
            _users = users;
            _key = GetSelfMonitoringKeyAsync(cache);

            _productModel = _cache.GetProductByName(SelfMonitoringProductName);

            _notificationsCenter = notificationCenter;

            var productVersion = Assembly.GetEntryAssembly()?.GetName().GetVersion();

            var options = new CollectorOptions
            {
                AccessKey = _key.ToString(),
                ClientName = SelfCollectorName,
                DataSender = this,
                PackageCollectPeriod = TimeSpan.FromSeconds(5)
            };


            _collector = new DataCollector(options).AddCustomLogger(new DataCollectorLoggerWrapper(_logger));

            if (OperatingSystem.IsWindows())
                _collector.Windows.AddAllDefaultSensors(productVersion);
            else
                _collector.Unix.AddAllDefaultSensors(productVersion);

            DbStatisticsSensors = new DatabaseSensorsStatistics(_collector, db, cache, config, optionsMonitor);
            DbSizeSensors = new DatabaseSensorsSize(_collector, db, config);
            WebRequestsSensors = new ClientStatisticsSensors(_collector);
            ApiTokenUsageSensors = new ApiTokenUsageSensors(_collector);
            BackupSensors = new BackupSensors(_collector);
            TreeValueCacheStatistics = new TreeValueChacheStatistics(_collector);
            TelegramBotStatistics = new TelegramBotStatistics(_collector);
            SlackChannelStatistics = new SlackChannelStatistics(_collector);
            MattermostChannelStatistics = new MattermostChannelStatistics(_collector);

            _cache.RequestProcessed += OnRequestProcessed;


            _notificationsCenter.TelegramBot.MessageSended += OnMessageSended;
            _notificationsCenter.TelegramBot.ErrorHandled += OnErrorHandled;
            _notificationsCenter.TelegramBot.MessageSending += OnMesageSending;

            _notificationsCenter.SlackChannel.MessageSended += OnSlackMessageSended;
            _notificationsCenter.SlackChannel.ErrorHandled += OnSlackErrorHandled;
            _notificationsCenter.SlackChannel.MessageSending += OnSlackMessageSending;

            _notificationsCenter.MattermostChannel.MessageSended += OnMattermostMessageSended;
            _notificationsCenter.MattermostChannel.ErrorHandled += OnMattermostErrorHandled;
            _notificationsCenter.MattermostChannel.MessageSending += OnMattermostMessageSending;


        }

        private void OnRequestProcessed(string name, int queueSize, int milliseconds) => TreeValueCacheStatistics.AddRequestProcessed(name, queueSize, milliseconds);

        private void OnMessageSended(string chat, string message) => TelegramBotStatistics.RegisterMessageSended(chat, message);

        private void OnMesageSending() => TelegramBotStatistics.RegisterMessageSending();

        private void OnErrorHandled(string message) => TelegramBotStatistics.RegisterError(message);

        private void OnSlackMessageSended(string destination, string message) => SlackChannelStatistics.RegisterMessageSended(destination, message);

        private void OnSlackMessageSending() => SlackChannelStatistics.RegisterMessageSending();

        private void OnSlackErrorHandled(string message) => SlackChannelStatistics.RegisterError(message);

        private void OnMattermostMessageSended(string destination, string message) => MattermostChannelStatistics.RegisterMessageSended(destination, message);

        private void OnMattermostMessageSending() => MattermostChannelStatistics.RegisterMessageSending();

        private void OnMattermostErrorHandled(string message) => MattermostChannelStatistics.RegisterError(message);


        public void Dispose()
        {
            _cache.RequestProcessed -= OnRequestProcessed;
            _notificationsCenter.TelegramBot.MessageSended -= OnMessageSended;
            _notificationsCenter.TelegramBot.ErrorHandled -= OnErrorHandled;
            _notificationsCenter.TelegramBot.MessageSending -= OnMesageSending;
            _notificationsCenter.SlackChannel.MessageSended -= OnSlackMessageSended;
            _notificationsCenter.SlackChannel.ErrorHandled -= OnSlackErrorHandled;
            _notificationsCenter.SlackChannel.MessageSending -= OnSlackMessageSending;
            _notificationsCenter.MattermostChannel.MessageSended -= OnMattermostMessageSended;
            _notificationsCenter.MattermostChannel.ErrorHandled -= OnMattermostErrorHandled;
            _notificationsCenter.MattermostChannel.MessageSending -= OnMattermostMessageSending;
            _collector?.Dispose();
        }

        internal async Task Start()
        {
            // BEFORE the collector's own start: the reset disposes the cached
            // sensor instances, and the collector's InitAsync sweep then
            // re-initializes every still-REGISTERED one — so the nodes that
            // rebuild on the next request are handed LIVE instances. Resetting
            // after the start would poison the rebuilt nodes with the
            // disposed instances the storage's path dedup hands back.
            // (Nodes created while the collector was STOPPING hold inert
            // instances — clearing them is the heal; the occupied paths
            // survive either way.)
            ApiTokenUsageSensors.ResetLiveNodes();

            await _collector.Start();
        }

        internal async Task Stop()
        {
            await _collector.Stop();
            ApiTokenUsageSensors.ResetLiveNodes();
        }


        internal void UpdateStatictics()
        {
            var now = DateTime.UtcNow;
            if (_lastUpdateDbSize is null || now - _lastUpdateDbSize.Value >= DbSizeUpdateInterval)
            {
                DbSizeSensors.SendInfo();
                _lastUpdateDbSize = now;
            }

            DbStatisticsSensors.SendInfo();

            TreeValueCacheStatistics.UpdateSensorsCount(_cache.SensorsCount);

            // Token-usage retention: rate sensors never idle on their own,
            // so a dead token's subtree is evicted here. The predicate is
            // COMPOSED (#1403 review, round 6): IsTokenLive alone says
            // nothing about the owner, and owner deletion invalidates the
            // credential without touching the token row — the sweep composes
            // the same way authentication does.
            ApiTokenUsageSensors.EvictDeadTokens(TokenUsageLiveness.Compose(_apiTokens, _users));
        }


        private static Guid GetSelfMonitoringKeyAsync(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProductAsync(SelfMonitoringProductName, Guid.Empty).Result;

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key;
        }

        public ValueTask<ConnectionResult> TestConnectionAsync()
        {
            return ValueTask.FromResult(ConnectionResult.Ok);
        }

        public async  ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            await SendDataInternalAsync(items);
            return new PackageSendingInfo();
        }

        public async ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            await SendDataInternalAsync(items);
            return new PackageSendingInfo();
        }

        private async ValueTask SendDataInternalAsync(IEnumerable<SensorValueBase> items)
        {
            try
            {
                await _cache.AddSensorValuesAsync(_key, _productModel.Id, items);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        public async ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
        {
            foreach (var command in commands)
            {
                if (command is AddOrUpdateSensorRequest apiRequest)
                {
                    var relatedPath = apiRequest.Path;
                    var sensorType = apiRequest.SensorType;

                    if (!_cache.TryGetSensorByPath(_productModel.Id, relatedPath, out var sensor) && sensorType is null)
                    {
                        _logger.Error($"{nameof(apiRequest.SensorType)} property is required, because sensor {relatedPath} doesn't exist");
                        continue;
                    }

                    var coreRequest = new SensorAddOrUpdateRequest(_productModel.Id, relatedPath)
                    {
                        Update = apiRequest.Convert(sensor?.Id ?? Guid.Empty, SelfCollectorName),
                        Type = sensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean,
                    };

                    await _cache.AddOrUpdateSensorAsync(coreRequest, token);
                }
            }

            return new PackageSendingInfo();
        }

        public async ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
        {
            await SendDataInternalAsync([file]);
            return new PackageSendingInfo();
        }
    }
}