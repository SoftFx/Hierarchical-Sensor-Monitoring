using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using SensorsService;
using Microsoft.Extensions.Logging;

namespace HSMServer.Services
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
        public override async Task<SensorResponse> GetSingleSensorInfo(SensorRequest request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            var certificate = httpContext.Connection.ClientCertificate;


            JobSensorData data = await _dataStorage.GetSensorDataAsync(request.MachineName, request.SensorName);
            SensorResponse response = Convert(data);
            return response;
        }

        public override Task<SensorsResponse> GetSensorsInfo(SensorsRequest request, ServerCallContext context)
        {
            List<JobSensorData> dataList =
                _dataStorage.GetSensorsData(request.MachineName, request.SensorName, request.N);
            SensorsResponse response = Convert(dataList);
            return Task.FromResult(response);
        }

        private SensorResponse Convert(JobSensorData data)
        {
            SensorResponse result = new SensorResponse
            {
                Comment = data?.Comment ?? string.Empty, Success = data?.Success ?? false, Ticks = data?.Time.Ticks ?? -1
            };
            return result;
        }

        private SensorsResponse Convert(List<JobSensorData> dataList)
        {
            SensorsResponse result = new SensorsResponse();
            var converted = dataList.Select(Convert);
            result.Sensors.AddRange(converted);
            return result;
        }
    }
}
