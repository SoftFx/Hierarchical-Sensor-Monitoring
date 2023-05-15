using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    [ApiController]
    [ApiExplorerSettings(GroupName = "Grafana (JSON)")]
    [Route("grafana/[controller]/[action]")]
    public class JsonDatasourceController : ControllerBase
    {
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;

        public JsonDatasourceController(ITreeValuesCache cache, TreeViewModel tree)
        {
            _cache = cache;
            _tree = tree;
        }


        /// <summary>
        /// with 200 status code response. Used for "Test connection" on the datasource config page.
        /// </summary>
        [HttpGet]
        [ActionName("")]
        public bool TestConnection() => true;


        /// <summary>
        /// to return available Products
        /// </summary>
        [HttpPost]
        [ActionName("metrics")]
        public string GetMetrics(MetricsRequest _)
        {
            var metrics = _tree.GetRootProducts().Select(u => new Metric(u.Name, u.Id));

            return JsonSerializer.Serialize(metrics, _options);
        }


        /// <summary>
        /// to return a list of metric payload options (Sensors paths and data formats).
        /// </summary>
        [HttpPost]
        [ActionName("metric-payload-options")]
        public string GetOptions(MetricPayloadOptionsRequest request)
        {
            var options = request.Name switch
            {
                Metric.SensorsPayloadName => GetSensorsOptions(Guid.Parse(request.Metric)),
                Metric.TypePayloadName => GetDataTypeOptions(request.Payload.Sensor),
                _ => throw new Exception($"Usupported option {request.Name}")
            };

            return JsonSerializer.Serialize(options, _options);
        }


        /// <summary>
        /// to return panel data or annotations
        /// </summary>
        [HttpPost]
        [ActionName("query")]
        public async Task<string> ReadHistory(QueryHistoryRequest request)
        {
            var historyResponse = new List<object>(1 << 2);

            foreach (var target in request.Targets)
            {
                var sensorId = target.Payload.Sensor;

                if (target.Payload.IsFull && TryGetSensor(sensorId, out var sensor))
                {
                    var sensorData = await _cache.GetSensorValuesPage(sensor.Id, request.Range.FromUtc, request.Range.ToUtc, request.MaxDataPoints).Flatten();

                    object sensorHistory = target.Payload.Type switch
                    {
                        PayloadOption.PointsDataTypeLabel => DataToResponse<HistoryDatapointsResponse>(sensorData, sensorId),
                        PayloadOption.TableDataTypeLabel => sensor.Type switch
                        {
                            SensorType.IntegerBar or SensorType.DoubleBar => DataToResponse<BarHistoryTableResponse>(sensorData, sensorId),
                            _ => DataToResponse<SimpleHistoryTableResponse>(sensorData, sensorId),
                        },
                        _ => null,
                    };

                    if (sensorHistory != null)
                        historyResponse.Add(sensorHistory);
                }
            }

            return JsonSerializer.Serialize(historyResponse, _options);
        }


        private static T DataToResponse<T>(List<BaseValue> rawData, string target) where T : BaseHistoryResponse, new()
        {
            var response = new T()
            {
                Target = target,
            };

            return (T)response.FillRows(rawData);
        }

        private List<PayloadOption> GetSensorsOptions(Guid productId)
        {
            var sensors = _tree.GetAllNodeSensors(productId);
            var options = new List<PayloadOption>(sensors.Count);

            foreach (var sensorId in sensors)
                if (_tree.Sensors.TryGetValue(sensorId, out var sensor) && sensor.Integration.HasGrafana())
                    options.Add(new PayloadOption(sensor.Path, sensorId.ToString()));

            return options;
        }

        private List<PayloadOption> GetDataTypeOptions(string sensorRawId)
        {
            var options = new List<PayloadOption>(2);

            if (TryGetSensor(sensorRawId, out var sensor))
            {
                if (sensor.IsDatapointFormatSupported)
                    options.Add(new PayloadOption(PayloadOption.PointsDataTypeLabel));

                if (sensor.IsTableFormatSupported)
                    options.Add(new PayloadOption(PayloadOption.TableDataTypeLabel));
            }

            return options;
        }

        private bool TryGetSensor(string rawId, out SensorNodeViewModel sensor)
        {
            sensor = default;

            return Guid.TryParse(rawId, out var sensorId) && _tree.Sensors.TryGetValue(sensorId, out sensor);
        }
    }
}
