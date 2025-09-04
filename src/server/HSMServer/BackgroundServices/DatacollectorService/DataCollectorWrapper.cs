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
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;
using HSMServer.Core.ApiObjectsConverters;


namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDataSender, IDisposable
    {
        private const string SelfCollectorName = "Self monitoring";
        public const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _collector;

        private readonly ITreeValuesCache _cache;

        private readonly ProductModel _productModel;


        private readonly Guid _key;
        private readonly Logger _logger;

        private DateTime? _lastUpdateDbSize = null;

        internal readonly TimeSpan DbSizeUpdateInterval = TimeSpan.FromDays(1);

        internal DatabaseSensorsStatistics DbStatisticsSensors { get; }

        internal ClientStatisticsSensors WebRequestsSensors { get; }

        internal DatabaseSensorsSize DbSizeSensors { get; }

        internal BackupSensors BackupSensors { get; }

        internal TreeValueChacheStatistics TreeValueCacheStatistics { get; } 


        public DataCollectorWrapper(ITreeValuesCache cache, IDatabaseCore db, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _logger = LogManager.GetLogger(GetType().Name);

            _cache = cache;
            _key = GetSelfMonitoringKeyAsync(cache);

            _productModel = _cache.GetProductByName(SelfMonitoringProductName);

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
            BackupSensors = new BackupSensors(_collector);
            TreeValueCacheStatistics = new TreeValueChacheStatistics(_collector);

            _cache.RequestProcessed += OnRequestProcessed;
        }

        private void OnRequestProcessed(string name, int queueSize, int milliseconds)
        {
            TreeValueCacheStatistics.AddRequestProcessed(name, queueSize, milliseconds);
        }

        public void Dispose()
        {
            _cache.RequestProcessed -= OnRequestProcessed;
            _collector?.Dispose();
        }

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


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
                        Type = sensorType?.Convert() ?? Core.Model.SensorType.Boolean,
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