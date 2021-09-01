using HSMServer.DataLayer;
using HSMServer.Products;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Cache;
using HSMServer.Configuration;
using HSMServer.Constants;
using Microsoft.Extensions.Logging;

namespace HSMServer.BackgroundTask
{
    public class OutdatedSensorService : WorkerServiceBase
    {
        private DateTime _lastChecked;
        private readonly TimeSpan _checkInterval = new TimeSpan(1, 0 , 0,0);
        private readonly IConfigurationProvider _configurationProvider;
        private readonly IValuesCache _cache;
        private readonly ILogger<OutdatedSensorService> _logger;
        public OutdatedSensorService(IDatabaseAdapter databaseAdapter, IProductManager productManager, IConfigurationProvider configurationProvider,
            IValuesCache cache, ILogger<OutdatedSensorService> logger) : base(databaseAdapter, productManager)
        {
            _configurationProvider = configurationProvider;
            _cache = cache;
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
                        var sensors = _productManager.GetProductSensors(product.Name);
                        foreach (var sensor in sensors)
                        {
                            var lastValue = _databaseAdapter.GetLastSensorValueOld(sensor.ProductName, sensor.Path);
                            if (lastValue == null)
                                continue;

                            if (DateTime.Now - lastValue.TimeCollected < expireInterval)
                                continue;

                            sensorsToRemove.Add((product.Name, sensor.Path));
                        }
                    }

                    foreach (var sensorToRemove in sensorsToRemove)
                    {
                        _productManager.RemoveSensor(sensorToRemove.Item1, sensorToRemove.Item2);
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
