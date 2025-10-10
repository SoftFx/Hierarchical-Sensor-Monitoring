using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Polly;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects;


namespace HSMDataCollector.Client.HttpsClient
{
    internal sealed class CommandHandler : BaseHandlers<CommandRequestBase>
    {
        protected override DelayBackoffType DelayStrategy => DelayBackoffType.Linear;

        protected override int MaxRequestAttempts => int.MaxValue;

        internal override object ConvertToRequestData(CommandRequestBase value) => value;

        public CommandHandler(HsmHttpsClient client, Endpoints endpoints, ICollectorLogger logger) : base(client, endpoints, logger)
        {
        }


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

        internal override async ValueTask HandleRequestResultAsync(HttpResponseMessage response, IEnumerable<CommandRequestBase> values, CancellationToken token)
        {
            try
            {
                if (response != null)
                {
                    var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var errors = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: token).ConfigureAwait(false);

                        foreach (var val in values)
                        {
                            var path = val.Path;
                            var hasError = errors.TryGetValue(path, out var error);

                            if (hasError)
                                _logger.Error($"Command request for {path} has been faulted. {error}.");
                            else
                                _logger.Info($"Command request for {path} has been accepted.");
                        }
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        _logger.Error($"Command request for has been faulted. Status Code: {response.StatusCode}, Status Text: {error}.");
                    }
                }
                else
                {
                    foreach (var val in values)
                        _logger.Error($"Command for {val.Path} has been canceled.");
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        internal override async ValueTask HandleRequestResultAsync(HttpResponseMessage response, CommandRequestBase value, CancellationToken token)
        {
            try
            {
                if (response == null)
                    _logger.Error($"Command for {value.Path} has been canceled.");
                else
                {
                    var isSuccess = response.IsSuccessStatusCode;

                    if (!isSuccess)
                    {
                        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        var error = await JsonSerializer.DeserializeAsync<string>(stream, cancellationToken: token).ConfigureAwait(false);

                        _logger.Error($"Command request for {value.Path} has been faulted. {error}.");
                    }
                    else
                    {
                        _logger.Info($"Command request for {value.Path} has been accepted.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}