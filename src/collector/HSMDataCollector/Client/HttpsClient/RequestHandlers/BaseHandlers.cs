using System;
using System.Collections.Generic;
using System.Linq;
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

        internal event Func<string, HttpContent, CancellationToken, Task<HttpResponseMessage>> SendRequestEvent;

        protected abstract DelayBackoffType DelayStrategy { get; }

        protected abstract int MaxRequestAttempts { get; }

        protected readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        protected int _timeout = 1000;

        protected BaseHandlers(Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;

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


        internal virtual Task HandleRequestResultAsync(HttpResponseMessage response, List<T> values) => Task.CompletedTask;

        internal virtual Task HandleRequestResultAsync(HttpResponseMessage response, T value) => Task.CompletedTask;


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

        public async Task SendAsync(List<T> values, CancellationToken token)
        {
            await _semaphore.WaitAsync(token).ConfigureAwait(false);

            try
            {

                await HandleRequestResultAsync(await BuildAndSendNewRequestAsync(values, token), values);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SendAsync(T value, CancellationToken token)
        {
            await _semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await HandleRequestResultAsync(await BuildAndSendNewRequestAsync(value, token), value);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private ValueTask<HttpResponseMessage> BuildAndSendNewRequestAsync(IEnumerable<T> values, CancellationToken token)
        {
            var rawData = values.Select(ConvertToRequestData);
            var request = new ClientRequestModel(rawData, GetUri(rawData));
            return SendRequest(request, token);
        }

        private ValueTask<HttpResponseMessage> BuildAndSendNewRequestAsync(T value, CancellationToken token)
        {
            var rawData = ConvertToRequestData(value);
            var request = new ClientRequestModel(rawData, GetUri(rawData));
            return SendRequest(request, token);
        }

        private async ValueTask<HttpResponseMessage> SendRequest(ClientRequestModel request, CancellationToken token)
        {
            try
            {
                var response = await _pipeline.ExecuteAsync(ExecutePipeline, request, token);

                _queue.ThrowPackageRequestInfo(new PackageSendingInfo(request.JsonMessage.Length, response));

                return response;
            }
            catch (Exception ex)
            {
                _queue.ThrowPackageRequestInfo(new PackageSendingInfo(request?.JsonMessage?.Length ?? 0, null, exception: ex.Message));
                _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException?.Message} | Id = {request.Id} Data = {request.JsonMessage}");

                return default;
            }
        }

        private async ValueTask<HttpResponseMessage> ExecutePipeline(ClientRequestModel request, CancellationToken token) =>
            await SendRequestEvent(request.Uri, request.GetContent(), token);


        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}