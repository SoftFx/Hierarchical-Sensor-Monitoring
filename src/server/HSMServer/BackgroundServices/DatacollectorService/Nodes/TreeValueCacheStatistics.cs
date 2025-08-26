using System;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;



namespace HSMServer.BackgroundServices
{
    public record TreeValueChacheStatistics
    {
        private const string QueueSize = "Size";
        private const string RequestsPerSecond = "Requests";
        private const string ProcessingTime = "Process Time";
        private const string SensorsCount = "Sensors count";
        private const string QueueNodeName = "Update Queue";
        private const string NodeName = "Cache";

        private IBarSensor<int> _queueSize { get; }
        private IMonitoringRateSensor _requestsPerSecond { get; }

        private IBarSensor<double> _processingTime { get; }

        private IInstantValueSensor<int> _sensorsCount { get; }


        public TreeValueChacheStatistics(IDataCollector collector)
        {
            _queueSize = collector.CreateIntBarSensor($"{NodeName}/{QueueNodeName}/{QueueSize}", new BarSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Count,
                Description = $"The sensor sends information about {QueueNodeName} {QueueSize}."
            });

            _requestsPerSecond = collector.CreateRateSensor($"{NodeName}/{QueueNodeName}/{RequestsPerSecond}", new RateSensorOptions 
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.ValueInSecond,
                PostDataPeriod = TimeSpan.FromMinutes(5),
                Description = $"The sensor sends information about {QueueNodeName} processed {RequestsPerSecond} per second."
            });

            _processingTime = collector.CreateDoubleBarSensor($"{NodeName}/{QueueNodeName}/{ProcessingTime}", new BarSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                Precision = 5,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Milliseconds,
                Description = $"The sensor sends information about {QueueNodeName} requests {ProcessingTime} in ms."
            });

            _sensorsCount = collector.CreateIntSensor($"{NodeName}/{SensorsCount}", new InstantSensorOptions 
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Count,
                Description = $"The sensor sends information about {SensorsCount}."
            });
        }

        public void AddRequestProcessed(int value, int milliseconds)
        {
            _queueSize.AddValue(value);
            _requestsPerSecond.AddValue(1);
            _processingTime.AddValue(milliseconds);
        }

        public void UpdateSensorsCount(int count) => _sensorsCount.AddValue(count);

    }
}
