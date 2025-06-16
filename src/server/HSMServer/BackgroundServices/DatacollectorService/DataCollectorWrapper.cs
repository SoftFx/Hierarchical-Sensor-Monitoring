using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NLog;
using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using Org.BouncyCastle.Utilities;
using HSMServer.Services;


namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDataSender, IDisposable
    {
        private const string SelfCollectorName = "Self monitoring";
        public const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _collector;

        private readonly ITreeValuesCache _cache;
        private readonly IUpdatesQueue _queue;

        private readonly IHtmlSanitizerService _sanitizer;

        private readonly string _key;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private DateTime? _lastUpdateDbSize = null;

        internal readonly TimeSpan DbSizeUpdateInterval = TimeSpan.FromDays(1);

        internal DatabaseSensorsStatistics DbStatisticsSensors { get; }

        internal ClientStatisticsSensors WebRequestsSensors { get; }

        internal DatabaseSensorsSize DbSizeSensors { get; }

        internal BackupSensors BackupSensors { get; }


        public DataCollectorWrapper(ITreeValuesCache cache, IDatabaseCore db, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor, IUpdatesQueue queue, IHtmlSanitizerService sanitizerService)
        {
            _cache = cache;
            _queue = queue;
            _key = GetSelfMonitoringKey(cache);

            _sanitizer = sanitizerService;

            var productVersion = Assembly.GetEntryAssembly()?.GetName().GetVersion();
            var loggerOptions = new LoggerOptions()
            {
                WriteDebug = false,
            };

            var options = new CollectorOptions
            {
                AccessKey = _key,
                ClientName = SelfCollectorName,
                DataSender = this,
                PackageCollectPeriod = TimeSpan.FromSeconds(1)
            };

            _collector = new DataCollector(options).AddNLog(loggerOptions);

            if (OperatingSystem.IsWindows())
                _collector.Windows.AddAllDefaultSensors(productVersion);
            else
                _collector.Unix.AddAllDefaultSensors(productVersion);

            DbStatisticsSensors = new DatabaseSensorsStatistics(_collector, db, cache, config, optionsMonitor);
            DbSizeSensors = new DatabaseSensorsSize(_collector, db, config);
            WebRequestsSensors = new ClientStatisticsSensors(_collector);
            BackupSensors = new BackupSensors(_collector);
        }

        public void Dispose() => _collector?.Dispose();

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


        internal void SendDbInfo()
        {
            var now = DateTime.UtcNow;
            if (_lastUpdateDbSize is null || now - _lastUpdateDbSize.Value >= DbSizeUpdateInterval)
            {
                DbSizeSensors.SendInfo();
                _lastUpdateDbSize = now;
            }

            DbStatisticsSensors.SendInfo();
        }


        private static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName, Guid.Empty);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }

        public ValueTask<ConnectionResult> TestConnectionAsync()
        {
            return ValueTask.FromResult(ConnectionResult.Ok);
        }

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            SendDataInternal(items);
            return ValueTask.FromResult(new PackageSendingInfo());
        }

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            SendDataInternal(items);
            return ValueTask.FromResult(new PackageSendingInfo());
        }

        private void SendDataInternal(IEnumerable<SensorValueBase> items)
        {
            foreach (var item in items)
            {
                try
                {
                    var value = new StoreInfo(_key, item.Path) { BaseValue = item.Convert(_sanitizer) };

                    if (value.BaseValue == null)
                        continue;

                    _queue.AddItem(value);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
        {
            foreach (var command in commands)
            {
                if (command is AddOrUpdateSensorRequest apiRequest)
                {
                    var relatedPath = apiRequest.Path;
                    var sensorType = apiRequest.SensorType;

                    if (!_cache.TryGetSensorByPath(SelfMonitoringProductName, relatedPath, out var sensor) && sensorType is null)
                    {
                        _logger.Error($"{nameof(apiRequest.SensorType)} property is required, because sensor {relatedPath} doesn't exist");
                        continue;
                    }

                    var coreRequest = new SensorAddOrUpdateRequestModel(_key, relatedPath)
                    {
                        Update = apiRequest.Convert(sensor?.Id ?? Guid.Empty, SelfCollectorName),
                        Type = sensorType?.Convert() ?? Core.Model.SensorType.Boolean,
                    };

                    if (!_cache.TryAddOrUpdateSensor(coreRequest, out var error))
                    {
                        _logger.Error(error);
                    }
                }
            }

            return ValueTask.FromResult(new PackageSendingInfo());
        }

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}