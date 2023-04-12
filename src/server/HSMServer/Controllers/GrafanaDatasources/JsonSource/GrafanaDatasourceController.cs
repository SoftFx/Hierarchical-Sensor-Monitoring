using HSMServer.Core.Cache;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class GrafanaDatasourceController : ControllerBase
    {
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;

        public GrafanaDatasourceController(ITreeValuesCache cache, TreeViewModel tree)
        {
            _cache = cache;
            _tree = tree;
        }


        [HttpGet]
        [ActionName("")]
        public bool TestConnection() => true;


        [HttpPost]
        [ActionName("metrics")]
        public string GetMetrics(MetricsRequest _)
        {
            var metrics = _tree.GetRootProducts().Select(u => new Metric(u.Name, u.Id));

            return JsonSerializer.Serialize(metrics, _options);
        }


        [HttpPost]
        [ActionName("metric-payload-options")]
        public string GetOptions(MetricPayloadOptionsRequest request)
        {
            if (request.Name == Metric.SensorsPayloadName)
            {
                var sensors = _tree.GetAllNodeSensors(Guid.Parse(request.Metric));

                var options = new List<PayloadOption>(1 << 5);

                foreach (var sensorId in sensors)
                    if (_tree.Sensors.TryGetValue(sensorId, out var sensor))
                    {
                        options.Add(new PayloadOption(sensor.Path, sensorId.ToString()));
                    }

                return JsonSerializer.Serialize(options, _options);
            }

            return string.Empty;
        }


        [HttpPost]
        [ActionName("query")]
        public string ReadHistory(QueryHistoryRequest request)
        {
            var responses = new List<object>();

            foreach (var target in request.Targets)
                if (target.Payload.IsFull && target.TryGetTargetAsId(out var productId) && _tree.Nodes.TryGetValue(productId, out var product))
                {
                    foreach (var sensorIdRaw in target.Payload.Sensors)
                        if (Guid.TryParse(sensorIdRaw, out var sensorId) && _tree.Sensors.TryGetValue(sensorId, out var sensor))
                        {

                            //if (target.Payload.Type == "Datapoints")
                            //{
                            //    var response = new HistoryDatapointsResponse()
                            //    {
                            //        Target = sensorId,
                            //        Datapoints = new List<long[]>(1 << 2),
                            //    };

                            //    foreach (var data in sensor.Data)
                            //    {
                            //        response.Datapoints.Add(new long[2]
                            //        {
                            //                data.Value,
                            //                new DateTimeOffset(data.Date).ToUnixTimeMilliseconds()
                            //        });
                            //    }

                            //    responses.Add(response);
                            //}

                            //if (target.Payload.Type == "Table")
                            //{
                            //    var response = new HistoryTableResponse()
                            //    {
                            //        Target = sensorId,
                            //        Rows = new List<object[]>(1 << 2),
                            //    };

                            //    foreach (var data in sensor.Data)
                            //    {
                            //        response.Rows.Add(new object[]
                            //        {
                            //                new DateTimeOffset(data.Date).ToUnixTimeMilliseconds(),
                            //                data.Value,
                            //                data.Comment,
                            //        });
                            //    }

                            //    responses.Add(response);
                            //}
                        }
                }

            return JsonSerializer.Serialize(responses, _options);
        }
    }
}
