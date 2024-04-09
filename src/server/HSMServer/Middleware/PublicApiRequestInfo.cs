using HSMSensorDataObjects;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Middleware
{
    public sealed class PublicApiRequestInfo
    {
        public ProductModel Product { get; init; }

        public AccessKeyModel Key { get; init; }

        public string CollectorName { get; init; }

        public string RemoteIP { get; init; }

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
