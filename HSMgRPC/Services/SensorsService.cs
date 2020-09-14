using System.Threading.Tasks;
using Grpc.Core;
using HSMgRPC.DataLayer;
using SensorsService;
using Microsoft.Extensions.Logging;
using ShortSensorData = SensorsService;

namespace HSMgRPC.Services
{
    public class SensorsService : Sensors.SensorsBase
    {
        private readonly ILogger<SensorsService> _logger;
        private readonly DatabaseClass _dataStorage;
        public SensorsService(ILogger<SensorsService> logger, DatabaseClass dataStorage)
        {
            _logger = logger;
            _dataStorage = dataStorage;
        }
        public override Task<ShortSensorData.ShortSensorData> GetSingleSensorInfo(SensorRequest request, ServerCallContext context)
        {
            //var httpContext = context.GetHttpContext();
            //var certificate = httpContext.Connection.ClientCertificate;

            return _dataStorage.GetSensorDataAsync(request.MachineName, request.SensorName);
        }

        public override Task<SensorsList> GetSensorsInfo(SensorsRequest request, ServerCallContext context)
        {
            return base.GetSensorsInfo(request, context);
        }
    }
}
