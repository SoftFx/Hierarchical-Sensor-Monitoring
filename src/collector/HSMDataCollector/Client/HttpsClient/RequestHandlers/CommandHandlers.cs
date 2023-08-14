using HSMDataCollector.Logging;
using HSMDataCollector.Requests;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects.SensorRequests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient
{
    internal class CommandHandler : BaseHandlers<PriorityRequest>
    {
        private readonly ICommandQueue _commandQueue;


        public CommandHandler(ICommandQueue queue, Endpoints endpoints, ICollectorLogger logger) : base(queue, endpoints, logger)
        {
            _commandQueue = queue;
        }


        internal override Task SendRequest(List<PriorityRequest> values)
        {
            async Task RegisterRequest<T>(T apiValue, string uri)
            {
                var response = await RequestToServer(apiValue, uri);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var errors = JsonConvert.DeserializeObject<JObject>(json);

                    //foreach (var val in values)
                    //    if (errors.TryGetValue(val.Request.Path, out var error))
                    //    {
                    //        var result = response.IsSuccessStatusCode && string.IsNullOrEmpty(error);

                    //        _commandQueue.SetResult(val.Key, result);
                    //    }

                    return;
                }

                foreach (var val in values)
                    _commandQueue.SetCancel(val.Key);
            }

            return RegisterRequest(values.Select(u => u.Request), _endpoints.CommandsList);
        }

        internal override Task SendRequest(PriorityRequest value)
        {
            async Task RegisterRequest<T>(T apiValue, string uri)
            {
                var response = await RequestToServer(apiValue, uri);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    _commandQueue.SetResult(value.Key, string.IsNullOrEmpty(json));
                }
                else
                    _commandQueue.SetCancel(value.Key);
            }

            switch (value.Request)
            {
                case AddOrUpdateSensorRequest sensorUpdate:
                    return RegisterRequest(sensorUpdate, _endpoints.AddOrUpdateSensor);
                default:
                    _commandQueue.SetCancel(value.Key);
                    _logger.Error($"Unsupported request type: {value.Request.GetType().Name}");

                    return Task.CompletedTask;
            }
        }
    }
}
