using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.BackgroundServices
{

    public record SelfCollectSensor
    {
        private const double KbDivisor = 1 << 10;
        
        public IBarSensor<double> SentBytes { get; set; }
        public IBarSensor<double> ReceiveBytes { get; set; }
        public IInstantValueSensor<double> SentSensors { get; set; }
        public IInstantValueSensor<double> ReceiveSensors { get; set; }
        

        public SelfCollectSensor(){}


        public void AddRequestData(HttpRequest request)
        {
            ReceiveBytes.AddValue((request.ContentLength ?? 0) / KbDivisor);
        }

        public void AddResponseResult(HttpResponse response)
        {
            SentBytes.AddValue((response.ContentLength ?? 0) / KbDivisor);
        }
        
        public void AddReceiveData(int count)
        {
            ReceiveSensors.AddValue(count);
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
                SentBytes = _collector.Create1MinDoubleBarSensor($"{ClientNode}/{id}/Sent bytes"),
                ReceiveBytes = _collector.Create1MinDoubleBarSensor($"{ClientNode}/{id}/Recv bytes"),
                SentSensors = _collector.CreateDoubleSensor($"{ClientNode}/{id}/Sent sensors"),
                ReceiveSensors = _collector.CreateDoubleSensor($"{ClientNode}/{id}/Recv sensors"),
            });
        }
    }
}
