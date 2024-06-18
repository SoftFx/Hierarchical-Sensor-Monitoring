using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;


namespace HSMDataCollector.Client.HttpsClient
{
    internal abstract class BaseHandlers<T> : IDisposable
    {
        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

        protected readonly ICollectorLogger _logger;
        protected readonly Endpoints _endpoints;

        protected abstract DelayBackoffType DelayStrategy { get; }

        protected abstract int MaxRequestAttempts { get; }

        protected int _timeout = 1000;

        protected HsmHttpsClient _client;

        protected BaseHandlers(HsmHttpsClient client, Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;
            _client = client;

            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = MaxRequestAttempts,
                BackoffType      = DelayStrategy,

                MaxDelay = TimeSpan.FromMinutes(2),
                Delay    = TimeSpan.FromSeconds(1),

                OnRetry  = LogException,
            };

            _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                            .AddRetry(retryOptions)
                            .Build();
        }


        internal virtual ValueTask HandleRequestResultAsync(HttpResponseMessage response, IEnumerable<T> values) => default;
        
        internal virtual ValueTask HandleRequestResultAsync(HttpResponseMessage response, T value) => default;


        internal abstract object ConvertToRequestData(T value);

        internal abstract string GetUri(object rawData);


        private ValueTask LogException(OnRetryArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Result != null)
                _logger.Error($"Failed to send data. Attempt number = {args.AttemptNumber}| Code = {args.Outcome.Result.StatusCode}");

            else if (args.Outcome.Exception != null)
                    _logger.Error($"Failed to send data. Attempt number = {args.AttemptNumber}| Exception = {args.Outcome.Exception.Message} Inner = {args.Outcome.Exception.InnerException.Message}");

            return default;
        }

        public async ValueTask SendAsync(IEnumerable<T> values, CancellationToken token)
        {
            await HandleRequestResultAsync(await SendRequestAsync(new HttpRequest<T>(values, GetUri(values)), token), values);
        }

        public async ValueTask SendAsync(T value, CancellationToken token)
        {
            await HandleRequestResultAsync(await SendRequestAsync(new HttpRequest<T>(value, GetUri(value)), token), value);
        }


        private async ValueTask<HttpResponseMessage> SendRequestAsync(HttpRequest<T> request, CancellationToken token)
        {
            try
            {
                var response = await _pipeline.ExecuteAsync(ExecutePipelineAsync, request, token);

                _client.ReportPackageInfo(new PackageSendingInfo(request.Length, response));

                return response;
            }
            catch (Exception ex)
            {
                _client.ReportPackageInfo(new PackageSendingInfo(request.Length, null, exception: ex.Message));
                _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException?.Message} | Data = {request.JsonMessage}");

                return default;
            }
        }

        private ValueTask<HttpResponseMessage> ExecutePipelineAsync(HttpRequest<T> request, CancellationToken token) =>
            _client.SendRequestAsync(request.Uri, request.GetContent(), token);


        public void Dispose()
        {
        }
    }
}