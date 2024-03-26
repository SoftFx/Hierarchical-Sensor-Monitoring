using HSMCommon.Extensions;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DatabaseStatistics : DatabaseBase
    {
        private readonly TimeSpan _periodicity = TimeSpan.FromDays(1); // TODO: should be initialized from optionsMonitor

        private readonly IFileSensor _dbStatistics;
        private readonly IInstantValueSensor<double> _heaviestSensors;

        private DateTime _nextStart;


        public DatabaseStatistics(IDataCollector collector, IDatabaseCore database, IOptionsMonitor<MonitoringOptions> optionsMonitor)
            : base(collector, database, optionsMonitor)
        {
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

        }
    }
}
