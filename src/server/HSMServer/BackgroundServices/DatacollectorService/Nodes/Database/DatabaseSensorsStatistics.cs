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
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DatabaseSensorsStatistics : DatabaseSensorsBase
    {
        private const string FullStatisticsSensorName = "Full sensors size statistics";
        private const string TopHeaviestSensorName = "Top heaviest sensors";

        private readonly TimeSpan _periodicity = TimeSpan.FromDays(1); // TODO: should be initialized from optionsMonitor
        private readonly int _heaviestSensorsCount = 10; // TODO: should be initialized from optionsMonitor
        private readonly int _maxSensorSizeMegabytes = 500;
        private readonly string _tempDirectory = Path.GetTempPath();

        private readonly ITreeValuesCache _cache;

        private readonly IFileSensor _dbStatistics;
        private readonly IInstantValueSensor<double> _heaviestSensors;

        private DateTime _nextStart;


        public DatabaseSensorsStatistics(IDataCollector collector, IDatabaseCore database,
            IOptionsMonitor<MonitoringOptions> optionsMonitor, ITreeValuesCache cache)
            : base(collector, database, optionsMonitor)
        {
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
                Description = $"File with extended statistics of sensors history database memory.", // TODO
            };

            return _collector.CreateFileSensor($"{NodeName}/{FullStatisticsSensorName}", options);
        }

        private IInstantValueSensor<double> CreateDoubleSensor()
        {
            var options = new InstantSensorOptions
            {
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = Unit.MB,
                Description = $"The heaviest sensors.", // TODO
                Alerts =
                [
                    AlertsFactory.IfValue(AlertOperation.GreaterThan, _maxSensorSizeMegabytes)
                                 .ThenSendNotification($"$comment sensor size in the database has exceeded $target $unit")
                                 .AndSetIcon(AlertIcon.Warning).Build()
                ],
            };

            return _collector.CreateDoubleSensor($"{NodeName}/{TopHeaviestSensorName}", options);
        }

        private void UpdateNextStart() => _nextStart = DateTime.UtcNow.Ceil(_periodicity);

        private async Task BuildStatistics()
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
                            await writer.WriteLineAsync($"{sensor.RootProductName},{sensor.Path},{sensorInfo.TotalSizeBytes},{sensorInfo.ValuesSizeBytes},{sensorInfo.DataCount}");

                            heaviestSensors.Enqueue(sensor.FullPath, sensorInfo.TotalSizeBytes);
                            if (heaviestSensors.Count > _heaviestSensorsCount)
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

            var heaviestSensorsCount = Math.Min(_heaviestSensorsCount, heaviestSensors.Count);
            for (var i = 0; i < heaviestSensorsCount; ++i)
            {
                if (heaviestSensors.TryDequeue(out var sensorPath, out var totalBytes))
                    _heaviestSensors.AddValue(GetRoundedDouble(totalBytes), sensorPath);
            }

            File.Delete(tempFilePath);
        }
    }
}
