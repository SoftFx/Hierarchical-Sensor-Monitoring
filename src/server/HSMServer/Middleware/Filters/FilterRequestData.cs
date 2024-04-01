using HSMSensorDataObjects;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Middleware
{
    public sealed class RequestData
    {
        public List<SensorData> Data { get; } = [];

        public ProductModel Product { get; set; }

        public AccessKeyModel Key { get; set; }

        public string CollectorName { get; set; }
        
        public int Count { get; set; } = 1;

        public string TelemetryPath { get; private set; }


        public void BuildTelemetryPath() => TelemetryPath = Key is null ? null : $"{Product.DisplayName}/{Key.DisplayName}/{CollectorName}";
    }

    public sealed class SensorData
    {
        public BaseRequest Request { get; init; }
        
        public string Path { get; init; }
        
        public string KeyId { get; init; }
        
        public Guid Id { get; set; }
    }
}
