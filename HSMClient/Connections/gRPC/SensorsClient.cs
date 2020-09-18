using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Grpc.Net.Client;
using SensorsService;

namespace HSMClient.Connections.gRPC
{
    public class SensorsClient : ConnectorBase
    {
        private Sensors.SensorsClient _sensorsClient;
        private string _sensorName;
        private string _machineName;
        public SensorsClient(string sensorsUrl, string sensorName, string machineName) : base(sensorsUrl)
        {
            GrpcChannel sensorsChannel = GrpcChannel.ForAddress(sensorsUrl);
            _sensorsClient = new Sensors.SensorsClient(sensorsChannel);
            _machineName = machineName;
            _sensorName = sensorName;
        }

        public override object Get()
        {
            SensorRequest request = new SensorRequest() { MachineName = _machineName, SensorName = _sensorName };
            SensorResponse result = _sensorsClient.GetSingleSensorInfo(request);
            return result;
        }
    }
}