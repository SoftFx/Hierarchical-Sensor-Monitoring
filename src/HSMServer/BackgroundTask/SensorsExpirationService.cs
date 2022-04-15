using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.Products;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    /// <summary>
    /// Derived from BackgroundService class, the service checks if the sensor value in cache has expired and enqueues and update
    /// with status=Warning 
    /// </summary>
    public class SensorsExpirationService : BackgroundService
    {
        /// <summary>
        /// The check is performed via this interval
        /// </summary>
        private readonly TimeSpan _checkInterval = new TimeSpan(0,0,5,0);
        /// <summary>
        /// Sensor values that had been updated less than this interval from now are not processed
        /// </summary>
        private readonly TimeSpan _minimumUpdateInterval = new TimeSpan(0,0,5,0);

        private readonly IValuesCache _cache;
        private readonly IProductManager _productManager;
        private readonly ISensorsInterface _sensorsInterface;
        private readonly IMonitoringUpdatesReceiver _updatesReceiver;
        private readonly ILogger<SensorsExpirationService> _logger;

        public SensorsExpirationService(IValuesCache valuesCache, IProductManager productManager,
            ISensorsInterface sensorsInterface, IMonitoringUpdatesReceiver updatesReceiver,
            ILogger<SensorsExpirationService> logger)
        {
            _cache = valuesCache;
            _productManager = productManager;
            _sensorsInterface = sensorsInterface;
            _updatesReceiver = updatesReceiver;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                var products = _productManager.Products;
                var productNamesList = products.Select(p => p.Name).ToList();
                var cachedValues = _cache.GetValues(productNamesList);
                DateTime utcNow = DateTime.UtcNow;
                foreach (var cachedValue in cachedValues)
                {
                    var lastUpdateInterval = utcNow - cachedValue.Time;
                    if (lastUpdateInterval < _minimumUpdateInterval)
                        continue;

                    SensorInfo info = _sensorsInterface.GetSensorInfo(cachedValue.Product, cachedValue.Path);
                    if (info.ExpectedUpdateInterval == TimeSpan.Zero)
                        continue;

                    if (lastUpdateInterval < info.ExpectedUpdateInterval)
                        continue;

                    SensorData statusObject = CreateStatusUpdateObject(cachedValue);
                    _updatesReceiver.AddUpdate(statusObject); // TODO: Add updates sensors statuses to cache
                }

                await Task.Delay(_checkInterval, stoppingToken);
            } while (!stoppingToken.IsCancellationRequested);
        }

        private SensorData CreateStatusUpdateObject(SensorData originalData)
        {
            SensorData clone = originalData.Clone();
            clone.Status = SensorStatus.Warning;
            clone.TransactionType = TransactionType.Update;
            clone.ValidationError = ValidationConstants.SensorValueOutdated;
            return clone;
        }
    }
}
