using HSMServer.Core.Model;
using System;

namespace HSMServer.Middleware
{
    public class FilterRequestData
    {
        public ProductModel Product { get; set; }
        
        public AccessKeyModel Key { get; set; }
        
        public string TelemetryPath { get; set; }
        
        public Guid SensorId { get; set; }
    }
}
