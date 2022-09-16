using HSMCommon.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using Microsoft.Extensions.Hosting;
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
    public class OutdatedSensorService : BackgroundService
    {
        private DateTime _lastChecked;

        private readonly TimeSpan _checkInterval = new TimeSpan(1, 0, 0, 0);

        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly ILogger<OutdatedSensorService> _logger;

        public OutdatedSensorService(ITreeValuesCache treeValuesCache,
            IConfigurationProvider configurationProvider, ILogger<OutdatedSensorService> logger)
        {
            _treeValuesCache = treeValuesCache;
            _configurationProvider = configurationProvider;
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
                        _configurationProvider.ReadOrDefault(nameof(ConfigurationConstants.SensorExpirationTime));
                    var expireInterval = TimeSpan.Parse(obj.Value);

                    var sensorsToRemove = new List<Guid>();

                    var sensors = _treeValuesCache.GetSensors();
                    foreach (var sensor in sensors)
                    {
                        if (!sensor.HasData || DateTime.Now - sensor.LastUpdateTime < expireInterval)
                            continue;

                        sensorsToRemove.Add(sensor.Id);
                    }

                    foreach (var sensorId in sensorsToRemove)
                        _treeValuesCache.RemoveSensorData(sensorId);

                    _logger.LogInformation($"{sensorsToRemove.Count} sensors removed.");
                    _lastChecked = DateTime.Now;
                }

                await Task.Delay(_checkInterval, stoppingToken);

            } while (!stoppingToken.IsCancellationRequested);
        }
    }
}
