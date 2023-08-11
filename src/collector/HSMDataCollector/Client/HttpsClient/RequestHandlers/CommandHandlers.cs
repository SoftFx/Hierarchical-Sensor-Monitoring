using HSMDataCollector.Logging;
using HSMDataCollector.Requests;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects.SensorRequests;
using Newtonsoft.Json;
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
                var response = await base.RequestToServer(apiValue, uri);
                var json = await response.Content.ReadAsStringAsync();
                var errors = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                foreach (var val in values)
                    if (errors.TryGetValue(val.Request.Path, out var error))
                    {
                        var result = response.IsSuccessStatusCode && string.IsNullOrEmpty(error);

                        _commandQueue.SetResult(val.Key, result);
                    }
            }

            return RegisterRequest(values.Select(u => u.Request), _endpoints.AddOrUpdateSensor);
        }

        internal override Task SendRequest(PriorityRequest value)
        {
            async Task RegisterRequest<T>(T apiValue, string uri)
            {
                var response = await base.RequestToServer(apiValue, uri);
                var json = await response.Content.ReadAsStringAsync();

                var result = response.IsSuccessStatusCode && string.IsNullOrEmpty(json);

                _commandQueue.SetResult(value.Key, result);
            }


            switch (value.Request)
            {
                case SensorUpdateRequest sensorUpdate:
                    return RegisterRequest(sensorUpdate, _endpoints.AddOrUpdateSensor);
                default:
                    _commandQueue.SetCancel(value.Key);
                    _logger.Error($"Unsupported request type: {value.Request.GetType().Name}");

                    return Task.CompletedTask;
            }
        }
    }
}
