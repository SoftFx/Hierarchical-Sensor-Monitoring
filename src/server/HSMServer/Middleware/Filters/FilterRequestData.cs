using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    public class RequestData
    {
        public List<SensorData> Data { get; set; } = new();

        public ProductModel Product { get; set; }

        public AccessKeyModel Key { get; set; }

        public string TelemetryPath { get; set; }

        public int Count { get; set; } = 1;
        
        public string MonitoringKey { get; set; }

        public RequestData() { }

        public RequestData(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("MonitoringKey", out var key))
                MonitoringKey = key;
        }
    }

    public class SensorData
    {
        public string Path { get; set; }
        
        public Guid Id { get; set; }
    }
}
