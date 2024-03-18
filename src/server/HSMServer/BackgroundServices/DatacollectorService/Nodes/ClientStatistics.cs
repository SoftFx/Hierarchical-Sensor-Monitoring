using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.BackgroundServices
{

    public record SelfCollectSensor
    {
        private const double KbDivisor = 1 << 10;
        
        public required IBarSensor<int> RequestCount { get; init; }
        public required IBarSensor<double> RequestSize { get; init; }
        public required IInstantValueSensor<double> RequestsCountPerSecond { get; init; } //should be params
        public required IInstantValueSensor<double> RequestsSizePerSecond { get; init; } //should be params
        public required IBarSensor<double> ResponseSize { get; init; }
        public required IInstantValueSensor<double> SensorUpdatesPerSecond { get; init; } //should be params
        public required IBarSensor<int> SensorUpdates { get; init; }
        

        public SelfCollectSensor(){}


        public void AddRequestData(HttpRequest request)
        {
            RequestCount.AddValue(1);
            RequestSize.AddValue((request.ContentLength ?? 0) / KbDivisor);
            RequestsCountPerSecond.AddValue(1);
            RequestsSizePerSecond.AddValue((request.ContentLength ?? 0) / KbDivisor);
        }

        public void AddResponseResult(HttpResponse response)
        {
            ResponseSize.AddValue((response.ContentLength ?? 0) / KbDivisor);
        }

        public void AddReceiveData(ActionExecutingContext context)
        {
            if (context.ActionArguments.TryGetValue("values", out var values) && values is List<SensorValueBase> list)
            {
                SensorUpdates.AddValue(list.Count);
                SensorUpdatesPerSecond.AddValue(list.Count);
            }
            else
            {
                SensorUpdates.AddValue(1);
                SensorUpdatesPerSecond.AddValue(1);
            }
        }

        public void AddReceiveData(int count)
        {
            SensorUpdates.AddValue(count);
            SensorUpdatesPerSecond.AddValue(count);
        }
    }
    public class ClientStatistics
    {        
        private readonly IDataCollector _collector;
        public readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor;

        
        public const string RequestsPerSecond = "Requests per second";
        public const string SensorUpdatesPerSecond = "Sensor Updates Per Second";

        public const string ResponseSize = "Response size";
        public const string RequestSizePerSecond = "Request size per second";


        public const string ClientNode = "Clients";
        public const string TotalGroup = "_Total";
        
        public const string RequestCount = "Request count";
        public const string RequestSize = "Request size";

        public const string SensorUpdates = "Sensor updates";

        public SelfCollectSensor this[string id]
        {
            get
            {
                if (SelfSensors.TryGetValue(id, out var sensor))
                {
                    return sensor;
                }
                    
                AddSensors(id);
                return SelfSensors.GetValueOrDefault(id);
            }
        }

        public SelfCollectSensor Total => SelfSensors.GetValueOrDefault(TotalGroup);


        public ConcurrentDictionary<string, SelfCollectSensor> SelfSensors { get; set; } = new();
        


        public ClientStatistics(IDataCollector collector, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;
            _optionsMonitor = optionsMonitor;
            
            AddSensors();
        }

        public void AddSensors(string path = null)
        {
            var id = path ?? TotalGroup;
            SelfSensors.TryAdd(id, new SelfCollectSensor
            {
                RequestCount = _collector.Create1MinIntBarSensor($"{ClientNode}/{id}/{RequestCount}"),
                RequestSize = _collector.Create1MinDoubleBarSensor($"{ClientNode}/{id}/{RequestSize}"),
                RequestsCountPerSecond = _collector.CreateRateSensor($"{ClientNode}/{id}/{RequestsPerSecond}"),
                RequestsSizePerSecond = _collector.CreateRateSensor($"{ClientNode}/{id}/{RequestSizePerSecond}"),
                ResponseSize = _collector.Create1MinDoubleBarSensor($"{ClientNode}/{id}/{ResponseSize}"),
                SensorUpdatesPerSecond = _collector.CreateRateSensor($"{ClientNode}/{id}/{SensorUpdatesPerSecond}"),
                SensorUpdates = _collector.Create1MinIntBarSensor($"{ClientNode}/{id}/{SensorUpdates}"),
            });
        }
    }
}
