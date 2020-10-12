using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.MonitoringServerCore;
using SensorsService;
using NLog;

namespace HSMServer.Services
{
    public class SensorsService : Sensors.SensorsBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;

        public SensorsService(MonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;

            _logger.Info("Sensors service started");
        }

        public override Task<SensorsUpdateMessage> GetMonitoringUpdates(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            return Task.FromResult(_monitoringCore.GetSensorUpdates(httpContext.Connection.ClientCertificate));
        }

        public override Task<SensorsTreeMessage> GetMonitoringTree(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            return Task.FromResult(_monitoringCore.GetSensorsTree(httpContext.Connection.ClientCertificate));
        }
    }
}
