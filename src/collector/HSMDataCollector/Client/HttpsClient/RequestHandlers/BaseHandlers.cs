using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;


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


        internal virtual ValueTask HandleRequestResultAsync(HttpResponseMessage response, IEnumerable<T> values, CancellationToken token) => default;
        
        internal virtual ValueTask HandleRequestResultAsync(HttpResponseMessage response, T value, CancellationToken token) => default;


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

        public async ValueTask<PackageSendingInfo> SendAsync(IEnumerable<T> values, CancellationToken token)
        {
            HttpRequest<T> request = new HttpRequest<T>(values, GetUri(values));
            try
            {
                var response = await _pipeline.ExecuteAsync(ExecutePipelineAsync, request, token).ConfigureAwait(false);
                await HandleRequestResultAsync(response, values, token).ConfigureAwait(false);
                return new PackageSendingInfo(request.Length, response);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException?.Message} | Data = {request.Content}");
                return new PackageSendingInfo(request.Length, null, exception: ex.Message);
            }
        }

        public async ValueTask<PackageSendingInfo> SendAsync(T value, CancellationToken token)
        {
            HttpRequest<T> request = new HttpRequest<T>(value, GetUri(value));
            try
            {
                var response = await _pipeline.ExecuteAsync(ExecutePipelineAsync, request, token).ConfigureAwait(false);
                await HandleRequestResultAsync(response, value, token).ConfigureAwait(false);
                return new PackageSendingInfo(request.Length, response);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException?.Message} | Data = {request.Content}");
                return new PackageSendingInfo(request.Length, null, exception: ex.Message);
            }
        }


        private ValueTask<HttpResponseMessage> ExecutePipelineAsync(HttpRequest<T> request, CancellationToken token) =>
            _client.SendRequestAsync(request.Uri, request.GetContent(), token);


        public void Dispose()
        {
        }
    }
}