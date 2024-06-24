using System;
using System.Collections.Generic;
using Polly;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Client.HttpsClient
{
    internal class DataHandlers : BaseHandlers<SensorValueBase>
    {
        protected override DelayBackoffType DelayStrategy => DelayBackoffType.Exponential;

        protected override int MaxRequestAttempts => 10;


        public DataHandlers(HsmHttpsClient client, Endpoints endpoints, ICollectorLogger logger) : base(client, endpoints, logger) { }


        internal override object ConvertToRequestData(SensorValueBase value) => value;

        internal override string GetUri(object rawData)
        {
            switch (rawData)
            {
                case IEnumerable<object> _:
                    return _endpoints.List;
                case BoolSensorValue _:
                    return _endpoints.Bool;
                case IntSensorValue _:
                    return _endpoints.Integer;
                case DoubleSensorValue _:
                    return _endpoints.Double;
                case StringSensorValue _:
                    return _endpoints.String;
                case TimeSpanSensorValue _:
                    return _endpoints.Timespan;
                case IntBarSensorValue _:
                    return _endpoints.IntBar;
                case DoubleBarSensorValue _:
                    return _endpoints.DoubleBar;
                case FileSensorValue _:
                    return _endpoints.File;
                case VersionSensorValue _:
                    return _endpoints.Version;
                case RateSensorValue _:
                    return _endpoints.Rate;
                default:
                    throw new Exception($"Unsupported sensor type: {((SensorValueBase)rawData).Path}");
            }
        }
    }
}