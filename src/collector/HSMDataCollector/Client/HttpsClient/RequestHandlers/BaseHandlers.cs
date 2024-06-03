using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient
{
    internal abstract class BaseHandlers<T> : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

        protected readonly ICollectorLogger _logger;
        protected readonly ISyncQueue<T> _queue;
        protected readonly Endpoints _endpoints;

        internal event Func<string, HttpContent, CancellationToken, Task<HttpResponseMessage>> SendRequestEvent;

        protected abstract DelayBackoffType DelayStrategy { get; }

        protected abstract int MaxRequestAttempts { get; }

        protected readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        protected int _timeout = 1000;

        protected BaseHandlers(ISyncQueue<T> queue, Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;
            _queue = queue;

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

            _queue.NewValuesEvent += RecieveDataQueue;
            _queue.NewValueEvent += RecieveDataQueue;
        }


        internal virtual Task HandleRequestResult(HttpResponseMessage response, List<T> values) => Task.CompletedTask;

        internal virtual Task HandleRequestResult(HttpResponseMessage response, T value) => Task.CompletedTask;


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

        private async Task RecieveDataQueue(List<T> values)
        {
            if (await _semaphore.WaitAsync(_timeout))
            {
                try
                {
                    await HandleRequestResult(await BuildAndSendNewRequest(values), values);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                foreach (var value in values)
                    _queue.AddFail(value);
            }
        }

        private async Task RecieveDataQueue(T value)
        {
            if (await _semaphore.WaitAsync(_timeout))
            {
                try
                {
                    await HandleRequestResult(await BuildAndSendNewRequest(value), value);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                _queue.AddFail(value);
            }
        }

        private Task<HttpResponseMessage> BuildAndSendNewRequest(IEnumerable<T> values)
        {
            var rawData = values.Select(ConvertToRequestData);
            var request = new ClientRequestModel(rawData, GetUri(rawData));
            return SendRequest(request);
        }

        private Task<HttpResponseMessage> BuildAndSendNewRequest(T value)
        {
            var rawData = ConvertToRequestData(value);
            var request = new ClientRequestModel(rawData, GetUri(rawData));
            return SendRequest(request);
        }

        private async Task<HttpResponseMessage> SendRequest(ClientRequestModel request)
        {
            try
            {
                var response = await _pipeline.ExecuteAsync(ExecutePipeline, request, _tokenSource.Token);

                _queue.ThrowPackageRequestInfo(new PackageSendingInfo(request.JsonMessage.Length, response));

                return response;
            }
            catch (Exception ex)
            {
                _queue.ThrowPackageRequestInfo(new PackageSendingInfo(request?.JsonMessage?.Length ?? 0, null, exception: ex.Message));
                _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException.Message} | Id = {request.Id} Data = {request.JsonMessage}");

                return default;
            }
        }

        private async ValueTask<HttpResponseMessage> ExecutePipeline(ClientRequestModel request, CancellationToken token) =>
            await SendRequestEvent(request.Uri, request.GetContent(), token);


        public void Dispose()
        {
            _queue.NewValuesEvent -= RecieveDataQueue;
            _queue.NewValueEvent  -= RecieveDataQueue;

            _tokenSource?.Cancel();
            _tokenSource.Dispose();

            _semaphore.Dispose();
        }
    }
}