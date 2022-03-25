using HSMCommon.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.Products;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    /// <summary>
    /// This class is derived from BackgroundService. Every day, all sensors are checked. Sensors with latest updated older
    /// than a specified parameter value are deleted
    /// </summary>
    public class OutdatedSensorService : WorkerServiceBase
    {
        private DateTime _lastChecked;
        private readonly TimeSpan _checkInterval = new TimeSpan(1, 0 , 0,0);
        private readonly IConfigurationProvider _configurationProvider;
        private readonly ISensorsInterface _sensorsInterface;
        private readonly IValuesCache _cache;
        private readonly ILogger<OutdatedSensorService> _logger;

        public OutdatedSensorService(IDatabaseCore databaseAdapter, IProductManager productManager, IConfigurationProvider configurationProvider,
            ISensorsInterface sensorsInterface, IValuesCache cache,
            ILogger<OutdatedSensorService> logger) : base(databaseAdapter, productManager)
        {
            _configurationProvider = configurationProvider;
            _cache = cache;
            _sensorsInterface = sensorsInterface;
            _logger = logger;
            _lastChecked = DateTime.MinValue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                DateTime currentDateTime = DateTime.Now;
                if (currentDateTime - _lastChecked > _checkInterval)
                {
                    ConfigurationObject obj =
                        _configurationProvider.ReadOrDefaultConfigurationObject(nameof(ConfigurationConstants
                            .SensorExpirationTime));
                    var expireInterval = TimeSpan.Parse(obj.Value);

                    List<(string, string)> sensorsToRemove = new List<(string, string)>();
                    var products = _productManager.Products;
                    foreach (var product in products)
                    {
                        var sensors = _sensorsInterface.GetProductSensors(product.Name);
                        foreach (var sensor in sensors)
                        {
                            //var lastValue = _databaseAdapter.GetLastSensorValueOld(sensor.ProductName, sensor.Path);
                            var lastValue = _databaseAdapter.GetLastSensorValue(sensor.ProductName, sensor.Path);
                            if (lastValue == null)
                                continue;

                            if (DateTime.Now - lastValue.TimeCollected < expireInterval)
                                continue;

                            sensorsToRemove.Add((product.Name, sensor.Path));
                        }
                    }

                    foreach (var sensorToRemove in sensorsToRemove)
                    {
                        _sensorsInterface.RemoveSensor(sensorToRemove.Item1, sensorToRemove.Item2);
                        _cache.RemoveSensorValue(sensorToRemove.Item1, sensorToRemove.Item2);
                    }

                    _logger.LogInformation($"{sensorsToRemove.Count} sensors removed.");
                    _lastChecked = DateTime.Now;
                }


                await Task.Delay(_checkInterval, stoppingToken);

            } while (!stoppingToken.IsCancellationRequested);
        }
    }
}
