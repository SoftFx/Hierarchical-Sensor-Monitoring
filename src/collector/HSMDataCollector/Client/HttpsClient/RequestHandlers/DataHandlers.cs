using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient
{
    internal class DataHandlers : BaseHandlers<SensorValueBase>
    {
        public DataHandlers(ISyncQueue<SensorValueBase> queue, Endpoints endpoints, ICollectorLogger logger) : base(queue, endpoints, logger) { }


        internal override Task SendRequest(SensorValueBase value)
        {
            switch (value)
            {
                case BoolSensorValue boolV:
                    return RequestToServer(boolV, _endpoints.Bool);
                case IntSensorValue intV:
                    return RequestToServer(intV, _endpoints.Integer);
                case DoubleSensorValue doubleV:
                    return RequestToServer(doubleV, _endpoints.Double);
                case StringSensorValue stringV:
                    return RequestToServer(stringV, _endpoints.String);
                case TimeSpanSensorValue timeSpanV:
                    return RequestToServer(timeSpanV, _endpoints.Timespan);
                case IntBarSensorValue intBarV:
                    return RequestToServer(intBarV, _endpoints.IntBar);
                case DoubleBarSensorValue doubleBarV:
                    return RequestToServer(doubleBarV, _endpoints.DoubleBar);
                case FileSensorValue fileV:
                    return RequestToServer(fileV, _endpoints.File);
                case VersionSensorValue versionV:
                    return RequestToServer(versionV, _endpoints.Version);
                case CounterSensorValue counterV:
                    return RequestToServer(counterV, _endpoints.Counter);
                default:
                    _logger.Error($"Unsupported sensor type: {value.Path}");
                    return Task.CompletedTask;
            }
        }

        internal override Task SendRequest(List<SensorValueBase> values) => RequestToServer(values.Cast<object>().ToList(), _endpoints.List);
    }
}