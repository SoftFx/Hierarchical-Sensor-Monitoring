using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Middleware
{
    public class FilterRequestData
    {
        public ProductModel Product { get; set; }
        
        public AccessKeyModel Key { get; set; }
        
        public string TelemetryPath { get; set; }

        public List<(string Path, Guid Id)> Data { get; set; } = new();
        
        public string Path { get; set; }
        
        public Guid SensorId { get; set; }

        public int Count { get; set; } = 0;
    }
}
