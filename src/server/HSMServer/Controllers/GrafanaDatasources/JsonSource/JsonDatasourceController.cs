using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
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
        public ActionResult<bool> TestConnection() => TryGetKey(out _, out var message) ? true : BadRequest(message);


        /// <summary>
        /// to return available Products
        /// </summary>
        [HttpPost]
        [ActionName("metrics")]
        public ActionResult<string> GetMetrics(MetricsRequest _)
        {
            if (!TryGetKey(out var key, out var message))
                return BadRequest(message);

            var products = key.IsMaster ? _cache.GetProducts() : new List<ProductModel>(1) { _cache.GetProduct(key.ProductId) };
            var metrics = products.OrderBy(p => p.DisplayName).Select(u => new Metric(u.DisplayName, u.Id));

            return JsonSerializer.Serialize(metrics, _options);
        }


        /// <summary>
        /// to return a list of metric payload options (Sensors paths and data formats).
        /// </summary>
        [HttpPost]
        [ActionName("metric-payload-options")]
        public ActionResult<string> GetOptions(MetricPayloadOptionsRequest request)
        {
            if (!TryGetKey(out _, out var message))
                return BadRequest(message);

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
        public async Task<ActionResult<string>> ReadHistory(QueryHistoryRequest request)
        {
            if (!TryGetKey(out _, out var message))
                return BadRequest(message);

            var historyResponse = new List<object>(1 << 2);

            foreach (var target in request.Targets)
            {
                var sensorId = target.Payload.Sensor;

                if (target.Payload.IsFull && TryGetSensor(sensorId, out var sensor))
                {
                    var sensorData = await GetSensorValues(request, sensor);

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

        private bool TryGetKey(out AccessKeyModel key, out string message)
        {
            key = null;
            message = null;

            Request.Headers.TryGetValue(nameof(HSMSensorDataObjects.BaseRequest.Key), out var keyStr);

            if (!string.IsNullOrEmpty(keyStr) && Guid.TryParse(keyStr, out var keyId))
            {
                key = _cache.GetAccessKey(keyId);

                if (key != null)
                    return true;
            }

            message = "Ivalid key";

            return false;
        }

        private async Task<List<BaseValue>> GetSensorValues(QueryHistoryRequest request, SensorNodeViewModel sensor)
        {
            var sensorValues = new List<BaseValue>(request.MaxDataPoints);
            var interval = new TimeSpan(0, 0, 0, 0, request.IntervalMs);

            await foreach (var page in _cache.GetSensorValuesPage(sensor.Id, request.Range.FromUtc, request.Range.ToUtc, TreeValuesCache.MaxHistoryCount))
            {
                foreach (var value in page)
                {
                    if (sensorValues.Count == 0 || (value.ReceivingTime - sensorValues[^1].ReceivingTime) >= interval)
                        sensorValues.Add(value);

                    if (sensorValues.Count == request.MaxDataPoints)
                        return sensorValues;
                }
            }

            return sensorValues;
        }
    }
}
