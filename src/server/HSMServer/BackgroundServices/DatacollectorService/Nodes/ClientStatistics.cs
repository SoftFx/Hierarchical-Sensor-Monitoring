using System;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMDataCollector.Options;

namespace HSMServer.BackgroundServices
{
    public record SelfCollectSensor
    {
        private const double KbDivisor = 1 << 10;

        public IInstantValueSensor<double> SentBytes { get; set; }
        public IInstantValueSensor<double> ReceiveBytes { get; set; }
        public IInstantValueSensor<double> SentSensors { get; set; }
        public IInstantValueSensor<double> ReceiveSensors { get; set; }

        public IInstantValueSensor<double> RPS { get; set; }


        public SelfCollectSensor() { }


        public void AddRequestData(HttpRequest request)
        {
            ReceiveBytes.AddValue((request.ContentLength ?? 0) / KbDivisor);
            RPS?.AddValue(1);
        }

        public void AddResponseResult(HttpResponse response)
        {
            SentBytes.AddValue((response.ContentLength ?? 0) / KbDivisor);
        }

        public void AddReceiveData(int count)
        {
            if (count != 0)
                ReceiveSensors.AddValue(count);
        }
    }

    public class ClientStatistics
    {
        private readonly IDataCollector _collector;
        public readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor;


        public const string RecvBytes = "Recv Bytes";
        public const string SentBytes = "Sent Bytes";
        public const string RecvSensors = "Recv Sensors";
        public const string SentSensors = "Sent Sensors";
        public const string RequestPerSecond = "RPS";
        public const string ClientNode = "Clients";
        public const string TotalGroup = "_Total";
        

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

        public SelfCollectSensor Total => this[TotalGroup];


        public ConcurrentDictionary<string, SelfCollectSensor> SelfSensors { get; set; } = new();


        public ClientStatistics(IDataCollector collector, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            _collector = collector;
            _optionsMonitor = optionsMonitor;
        }

        public void AddSensors(string path = null)
        {
            var id = path ?? TotalGroup;
            var selfCollect = new SelfCollectSensor
            {
                SentBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentBytes}",
                    "Number of bytes that were sent from server to client"),
                ReceiveBytes = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvBytes}",
                    "Number of bytes that were received from client"),
                SentSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentSensors}",
                    "Number of sensors that were sent from server to client"),
                ReceiveSensors = _collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvSensors}",
                    "Number of sensors that were received from client"),
            };

            if (path is TotalGroup)
                selfCollect.RPS = _collector.CreateRateSensor($"{ClientNode}/{id}/{RequestPerSecond}",
                    new RateSensorOptions(){Description = "Number of requests that were received", PostDataPeriod = TimeSpan.FromMinutes(1)});

            SelfSensors.TryAdd(id, selfCollect);
        }
    }
}