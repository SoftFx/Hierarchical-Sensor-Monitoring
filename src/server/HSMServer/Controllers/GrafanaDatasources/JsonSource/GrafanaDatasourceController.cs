using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
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
            WriteIndented = true
        };


        [HttpGet]
        [ActionName("")]
        public string Index()
        {
            return "Ok";
        }


        [HttpPost]
        [ActionName("metrics")]
        public string GetMetrics(MetricsRequest _)
        {
            //var payloads = _storage.Payloads.Values.ToArray();

            //var list = _storage.Products.Select(p => new MainMetric(p.Name, p.Id).Init(payloads));


            return JsonSerializer.Serialize(new Metric("Test", "1"), _options);
        }


        [HttpPost]
        [ActionName("metric-payload-options")]
        public string GetOptions(MetricPayloadOptionsRequest request)
        {
            var items = new List<PayloadOption>(1 << 2);

            //var product = _storage.Products.FirstOrDefault(u => u.Id == request.Metric);

            //if (product != null && request.Name == "Sensors")
            //    items.AddRange(product.Sensors.Select(u => new PayloadItem(u.Path, u.Id)));

            return JsonSerializer.Serialize(items, _options);
        }


        [HttpPost]
        [ActionName("query")]
        public string ReadHistory(QueryHistoryRequest request)
        {
            var responses = new List<object>();

            //foreach (var target in request.Targets)
            //    if (target.Payload.Sensors != null && target.Payload.Type != null)
            //    {
            //        foreach (var sensorId in target.Payload.Sensors)
            //        {
            //            var product = _storage.Products.FirstOrDefault(u => u.Id == target.Target);

            //            if (product != null)
            //            {
            //                var sensor = product.Sensors.FirstOrDefault(u => u.Id == sensorId);

            //                if (sensor != null)
            //                {
            //                    if (target.Payload.Type == "Datapoints")
            //                    {
            //                        var response = new HistoryDatapointsResponse()
            //                        {
            //                            Target = sensorId,
            //                            Datapoints = new List<long[]>(1 << 2),
            //                        };

            //                        foreach (var data in sensor.Data)
            //                        {
            //                            response.Datapoints.Add(new long[2]
            //                            {
            //                                data.Value,
            //                                new DateTimeOffset(data.Date).ToUnixTimeMilliseconds()
            //                            });
            //                        }

            //                        responses.Add(response);
            //                    }

            //                    if (target.Payload.Type == "Table")
            //                    {
            //                        var response = new HistoryTableResponse()
            //                        {
            //                            Target = sensorId,
            //                            Rows = new List<object[]>(1 << 2),
            //                        };

            //                        foreach (var data in sensor.Data)
            //                        {
            //                            response.Rows.Add(new object[]
            //                            {
            //                                new DateTimeOffset(data.Date).ToUnixTimeMilliseconds(),
            //                                data.Value,
            //                                data.Comment,
            //                            });
            //                        }

            //                        responses.Add(response);
            //                    }
            //                }
            //            }
            //        }
            //    }

            return JsonSerializer.Serialize(responses, _options);
        }
    }
}
