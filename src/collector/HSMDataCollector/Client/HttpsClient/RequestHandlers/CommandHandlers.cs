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
    internal sealed class CommandHandler : BaseHandlers<PriorityRequest>
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

                if (response == null)
                {
                    foreach (var val in values)
                        _commandQueue.SetCancel(val.Key);

                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var errors = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                foreach (var val in values)
                {
                    var path = val.Request.Path;
                    var hasError = errors.TryGetValue(path, out var error);

                    if (hasError)
                        _logger.Error($"Error command for {path} - {error}");

                    _commandQueue.SetResult(val.Key, !hasError);
                }
            }

            return RegisterRequest(values.Select(u => u.Request), _endpoints.CommandsList);
        }

        internal override Task SendRequest(PriorityRequest value)
        {
            async Task RegisterRequest<T>(T apiValue, string uri)
            {
                var response = await RequestToServer(apiValue, uri);

                if (response == null)
                    _commandQueue.SetCancel(value.Key);
                else
                {
                    var isSuccess = response.IsSuccessStatusCode;

                    if (!isSuccess)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var error = JsonConvert.DeserializeObject<string>(json);

                        _logger.Error($"Error command for {value.Request.Path} - {error}");
                    }

                    _commandQueue.SetResult(value.Key, isSuccess);
                }
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
