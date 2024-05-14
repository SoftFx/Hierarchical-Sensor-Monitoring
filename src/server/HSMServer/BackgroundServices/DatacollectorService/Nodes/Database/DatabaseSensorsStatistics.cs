using HSMCommon.Extensions;
using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.StatisticInfo;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DatabaseSensorsStatistics : DatabaseSensorsBase
    {
        private const int MaxSensorSizeMegabytes = 500;
        public const string TopHeaviestSensorName = "Top heaviest sensors";
        public const string FullStatisticsSensorName = "Full sensors size statistics";

        private readonly string _tempDirectory = Path.GetTempPath();

        private readonly ITreeValuesCache _cache;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly IFileSensor _dbStatistics;
        private readonly IInstantValueSensor<double> _heaviestSensors;

        private DateTime _nextStart;
        private int _databaseStatisticsPeriodDays;


        private TimeSpan Periodicity => TimeSpan.FromDays(_databaseStatisticsPeriodDays);

        private int HeaviestSensorsCount => _serverConfig.MonitoringOptions.TopHeaviestSensorsCount;


        internal DatabaseSensorsStatistics(IDataCollector collector, IDatabaseCore database, ITreeValuesCache cache, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor)
            : base(collector, database, config)
        {
            _databaseStatisticsPeriodDays = _serverConfig.MonitoringOptions.DatabaseStatisticsPeriodDays;
            optionsMonitor.OnChange(MonitoringOptionsListener);

            _cache = cache;

            _dbStatistics = CreateFileSensor();
            _heaviestSensors = CreateDoubleSensor();

            UpdateNextStart();
        }


        internal override void SendInfo()
        {
            if (DateTime.UtcNow < _nextStart)
                return;

            UpdateNextStart();

            _ = BuildStatistics();
        }


        private IFileSensor CreateFileSensor()
        {
            var options = new FileSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                KeepHistory = TimeSpan.FromDays(7),
                Description = $"This sensor sends information about extended database memory statistics that sensors history occupies. " +
                              $"It is a file in CSV format that has 5 columns: Product, Path, Total size in bytes (number of bytes occupied by sensor keys and values ​​in the sensors history database), " +
                              $"Values size in bytes (number of bytes occupied by sensor values only ​​in the sensor history database), Data count (number of sensor historical records). " +
                              $"The memory check is carried out every {_serverConfig.MonitoringOptions.DatabaseStatisticsPeriodDays} day(s).",
            };

            return _collector.CreateFileSensor($"{NodeName}/{FullStatisticsSensorName}", options);
        }

        private IInstantValueSensor<double> CreateDoubleSensor()
        {
            const Unit sensorUnit = Unit.MB;

            var options = new InstantSensorOptions
            {
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = sensorUnit,
                Description = $"This sensor sends information about the top {HeaviestSensorsCount} heaviest sensors (sensors that take up the most database memory) in {sensorUnit}. " +
                              $"The memory check is carried out every {_serverConfig.MonitoringOptions.DatabaseStatisticsPeriodDays} day(s).",
                Alerts =
                [
                    AlertsFactory.IfValue(AlertOperation.GreaterThan, MaxSensorSizeMegabytes)
                                 .ThenSendNotification($"$comment sensor size in the database has exceeded $target $unit")
                                 .AndSetIcon(AlertIcon.Warning).Build()
                ],
            };

            return _collector.CreateDoubleSensor($"{NodeName}/{TopHeaviestSensorName}", options);
        }

        private void UpdateNextStart() => _nextStart = DateTime.UtcNow.Ceil(Periodicity) + Periodicity - TimeSpan.FromDays(1);

        private async Task BuildStatistics()
        {
            try
            {
                var tempFilePath = Path.Combine(_tempDirectory, $"database_stats_{DateTime.UtcNow.ToWindowsDateFormat()}.csv");
                var heaviestSensors = new PriorityQueue<string, long>();

                await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite))
                {
                    await using var writer = new StreamWriter(stream);


                    async Task WriteStats(NodeHistoryInfo nodeInfo)
                    {
                        foreach (var (sensorId, sensorInfo) in nodeInfo.SensorsInfo)
                        {
                            var sensor = _cache.GetSensor(sensorId);

                            if (sensor is not null)
                            {
                                await writer.WriteLineAsync($"""
                                                             "{sensor.RootProductName}","{sensor.Path}","{sensorInfo.TotalSizeBytes}","{sensorInfo.ValuesSizeBytes}","{sensorInfo.DataCount}"
                                                             """);

                                heaviestSensors.Enqueue(sensor.FullPath, sensorInfo.TotalSizeBytes);
                                if (heaviestSensors.Count > HeaviestSensorsCount)
                                    heaviestSensors.Dequeue();
                            }
                        }

                        foreach (var (_, subnodeInfo) in nodeInfo.SubnodesInfo)
                            await WriteStats(subnodeInfo);
                    }


                    await writer.WriteLineAsync("Product,Path,Total size (bytes),Values size (bytes),Data count");

                    foreach (var product in _cache.GetProducts())
                        await WriteStats(_cache.GetNodeHistoryInfo(product.Id));
                }

                await _dbStatistics.SendFile(tempFilePath);

                var heaviestSensorsCount = Math.Min(HeaviestSensorsCount, heaviestSensors.Count);
                for (var i = 0; i < heaviestSensorsCount; ++i)
                {
                    if (heaviestSensors.TryDequeue(out var sensorPath, out var totalBytes))
                        _heaviestSensors.AddValue(GetRoundedDouble(totalBytes), sensorPath);
                }

                File.Delete(tempFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error building sensors size statistics: {ex.Message}");
            }
        }

        private void MonitoringOptionsListener(MonitoringOptions options, string __)
        {
            if (options.DatabaseStatisticsPeriodDays != _databaseStatisticsPeriodDays)
            {
                var previousStart = _nextStart - Periodicity;

                _databaseStatisticsPeriodDays = options.DatabaseStatisticsPeriodDays;
                _nextStart = previousStart + Periodicity;
            }
        }
    }
}
