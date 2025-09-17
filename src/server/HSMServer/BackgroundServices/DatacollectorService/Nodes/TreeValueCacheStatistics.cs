using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;


namespace HSMServer.BackgroundServices
{
    public record TreeValueChacheStatistics
    {
        private record CacheQueueStatistics
        {
            private const string QueueSize = "Size";
            private const string RequestsPerSecond = "Requests";
            private const string ProcessingTime = "Process Time";
            private const string QueueNodeName = "Update Queues";
            private const string NodeName = "Cache";

            private IBarSensor<int> _queueSize { get; }
            private IMonitoringRateSensor _requestsPerSecond { get; }

            private IBarSensor<double> _processingTime { get; }

            public string Name { get; }

            public CacheQueueStatistics(IDataCollector collector, string name)
            {
                Name = name;

                _queueSize = collector.CreateIntBarSensor($"{NodeName}/{QueueNodeName}/{Name}/{QueueSize}", new BarSensorOptions
                {
                    Alerts = [],
                    TTL = TimeSpan.MaxValue,
                    EnableForGrafana = false,
                    SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Count,
                    Description = $"The sensor sends information about {QueueNodeName} [{Name}] {QueueSize}."
                });

                _requestsPerSecond = collector.CreateRateSensor($"{NodeName}/{QueueNodeName}/{Name}/{RequestsPerSecond}", new RateSensorOptions
                {
                    Alerts = [],
                    TTL = TimeSpan.MaxValue,
                    EnableForGrafana = false,
                    SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.ValueInSecond,
                    PostDataPeriod = TimeSpan.FromMinutes(5),
                    Description = $"The sensor sends information about {QueueNodeName} [{Name}] processed {RequestsPerSecond} per second."
                });

                _processingTime = collector.CreateDoubleBarSensor($"{NodeName}/{QueueNodeName}/{Name}/{ProcessingTime}", new BarSensorOptions
                {
                    Alerts = [],
                    TTL = TimeSpan.MaxValue,
                    EnableForGrafana = false,
                    Precision = 5,
                    SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Milliseconds,
                    Description = $"The sensor sends information about {QueueNodeName} [{Name}] requests {ProcessingTime} in ms."
                });
            }

            public void AddRequestProcessed(int value, int milliseconds)
            {
                _queueSize.AddValue(value);
                _requestsPerSecond.AddValue(1);
                _processingTime.AddValue(milliseconds);
            }
        }


        private const string SensorsCount = "Sensors count";
        private const string NodeName = "Cache";


        private readonly IDataCollector _collector;

        private ConcurrentDictionary<string, CacheQueueStatistics> _queueStatistics = new(StringComparer.Ordinal);

        private IInstantValueSensor<int> _sensorsCount { get; }


        public TreeValueChacheStatistics(IDataCollector collector)
        {
            _collector = collector;

            _sensorsCount = _collector.CreateIntSensor($"{NodeName}/{SensorsCount}", new InstantSensorOptions 
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.Count,
                Description = $"The sensor sends information about {SensorsCount}."
            });
        }

        public void AddRequestProcessed(string name, int value, int milliseconds)
        {
             _queueStatistics.GetOrAdd(name, new CacheQueueStatistics(_collector, name)).AddRequestProcessed(value, milliseconds);
        }

        public void UpdateSensorsCount(int count) => _sensorsCount.AddValue(count);

    }
}
