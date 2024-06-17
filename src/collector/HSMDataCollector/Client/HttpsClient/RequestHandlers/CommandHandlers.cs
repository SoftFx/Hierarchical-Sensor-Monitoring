using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Polly;
using HSMDataCollector.Logging;
using HSMDataCollector.Requests;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Client.HttpsClient
{
    internal sealed class CommandHandler : BaseHandlers<PriorityRequest>
    {

        protected override DelayBackoffType DelayStrategy => DelayBackoffType.Linear;

        protected override int MaxRequestAttempts => int.MaxValue;


        public CommandHandler(Endpoints endpoints, ICollectorLogger logger) : base(endpoints, logger)
        {
        }


        internal override object ConvertToRequestData(PriorityRequest value) => value.Request;

        internal override string GetUri(object rawData)
        {
            switch (rawData)
            {
                case IEnumerable<object> _:
                    return _endpoints.CommandsList;
                case AddOrUpdateSensorRequest _:
                    return _endpoints.AddOrUpdateSensor;
                default:
                    throw new Exception($"Unsupported command type {rawData.GetType().FullName}");
            }
        }

        internal override async Task HandleRequestResultAsync(HttpResponseMessage response, List<PriorityRequest> values)
        {
            if (response != null)
            {
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
            else
            {
                foreach (var val in values)
                    _commandQueue.SetCancel(val.Key);
            }
        }

        internal override async Task HandleRequestResultAsync(HttpResponseMessage response, PriorityRequest value)
        {
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
    }
}