using HSMCommon.Extensions;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DatabaseStatistics : DatabaseBase
    {
        private readonly ITreeValuesCache _cache;
        private readonly TimeSpan _periodicity = TimeSpan.FromDays(1); // TODO: should be initialized from optionsMonitor

        private readonly IFileSensor _dbStatistics;
        private readonly IInstantValueSensor<double> _heaviestSensors;

        private DateTime _nextStart;


        public DatabaseStatistics(IDataCollector collector, IDatabaseCore database,
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
                KeepHistory = TimeSpan.FromDays(7),
                Description = $"File with extended statistics of sensors history database memory.",

                DefaultFileName = "Database statistics",
                Extension = "csv",
            };

            return _collector.CreateFileSensor($"{NodeName}/Sensors statistics", options);
        }

        private IInstantValueSensor<double> CreateDoubleSensor()
        {
            var options = new InstantSensorOptions // TODO: Grafana??
            {
                Alerts = [], // TODO: alert '> 800 mb' should be added
                TTL = TimeSpan.MaxValue,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,
                Description = $"The heaviest sensors.",
            };

            return _collector.CreateDoubleSensor($"{NodeName}/The heaviest sensors", options);
        }

        private void UpdateNextStart() => _nextStart = DateTime.UtcNow.Ceil(_periodicity);

        private async Task BuildStatistics()
        {
            Directory.CreateDirectory($"{Environment.CurrentDirectory}/stats");
            await using (var stream = new FileStream($"{Environment.CurrentDirectory}/stats/temp.csv", FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                await using var writer = new StreamWriter(stream);

                await writer.WriteLineAsync("Product,Path,Total,Values,Count");

                foreach (var product in _cache.GetProducts())
                {
                    var info = _cache.GetNodeHistoryInfo(product.Id);
                }
            }

            var result = await _dbStatistics.SendFile($"{Environment.CurrentDirectory}/stats/temp.csv");
        }
    }
}
