using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Middleware
{
    public class RequestData
    {
        public List<SensorData> Data { get; set; } = new();

        public ProductModel Product { get; set; }

        public AccessKeyModel Key { get; set; }

        public string TelemetryPath { get; set; }

        public int Count { get; set; } = 1;
        

        public RequestData() { }
    }

    public class SensorData
    {
        public string Path { get; set; }
        
        public string KeyId { get; set; }
        
        public AccessKeyModel Key { get; set; }
        
        public Guid Id { get; set; }
    }
}
