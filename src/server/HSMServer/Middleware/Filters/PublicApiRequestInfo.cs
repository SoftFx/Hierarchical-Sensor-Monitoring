using HSMSensorDataObjects;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Middleware
{
    public sealed class PublicApiRequestInfo
    {
        //public List<SensorData> Data { get; } = [];

        public ProductModel Product { get; set; }

        public AccessKeyModel Key { get; set; }

        public string CollectorName { get; set; }

        public string RemoteIP { get; set; }

        //public int Count { get; set; } = 1;

        public string TelemetryPath { get; private set; }


        public void BuildTelemetryPath() => TelemetryPath = Key is null ? null : $"{Product?.DisplayName}/{Key.DisplayName}/{CollectorName}";
    }


    public sealed class SensorData
    {
        public BaseRequest Request { get; }

        public string Path { get; }

        public string KeyId { get; }

        public Guid Id { get; set; }


        public SensorData(BaseRequest request)
        {
            Request = request;
            Path = request?.Path;
            KeyId = request?.Key;
        }
    }
}
