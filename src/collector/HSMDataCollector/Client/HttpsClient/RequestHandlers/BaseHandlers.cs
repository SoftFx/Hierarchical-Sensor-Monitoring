using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient
{
    internal abstract class BaseHandlers<T> : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly StringBuilder _retryLogBuilder = new StringBuilder(1 << 10);

        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

        private ClientRequestModel _currentRequest;

        protected readonly ICollectorLogger _logger;
        protected readonly ISyncQueue<T> _queue;
        protected readonly Endpoints _endpoints;

        internal event Func<string, HttpContent, CancellationToken, Task<HttpResponseMessage>> SendRequestEvent;


        protected abstract DelayBackoffType DelayStrategy { get; }

        protected abstract int MaxRequestAttempts { get; }


        private bool CanSendRequest => _currentRequest == null;


        protected BaseHandlers(ISyncQueue<T> queue, Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;
            _queue = queue;

            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = MaxRequestAttempts,
                BackoffType = DelayStrategy,

                MaxDelay = TimeSpan.FromMinutes(2),
                Delay = TimeSpan.FromSeconds(1),

                OnRetry = LogException,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                                  .HandleResult(result => result?.StatusCode.IsRetryCode() ?? true),
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
            if (!CanSendRequest)
            {
                var curAttempt = args.AttemptNumber + 1;

                _retryLogBuilder.Clear();
                _retryLogBuilder.Append($"Failed to send data. Code = {args.Outcome.Result?.StatusCode} ")
                                .Append($"Id = {_currentRequest.Id} ")
                                .Append($"Attempt number = {curAttempt} ");

                if (curAttempt == 1)
                    _retryLogBuilder.Append($"Uri = {_currentRequest.Uri} ")
                                    .Append($"Data = {_currentRequest.JsonMessage}");

                _logger.Error(_retryLogBuilder.ToString());
            }

            return default;
        }


        private async Task RecieveDataQueue(List<T> values)
        {
            if (CanSendRequest)
            {
                var requests = values.Select(ConvertToRequestData).ToList();

                await HandleRequestResult(await BuildAndSendNewRequest(requests), values);

                return;
            }

            foreach (var value in values)
                _queue.AddFail(value);
        }

        private async Task RecieveDataQueue(T value)
        {
            if (CanSendRequest)
                await HandleRequestResult(await BuildAndSendNewRequest(ConvertToRequestData(value)), value);
            else
                _queue.AddFail(value);
        }

        private async Task<HttpResponseMessage> BuildAndSendNewRequest(object rawData)
        {
            try
            {
                _currentRequest = new ClientRequestModel(rawData, GetUri(rawData));

                var response = await _pipeline.ExecuteAsync(ExecutePipeline, _tokenSource.Token);

                _queue.ThrowPackageRequestInfo(new PackageSendingInfo(_currentRequest.JsonMessage.Length, response));
                _currentRequest = null;

                return response;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                return null;
            }
        }

        private async ValueTask<HttpResponseMessage> ExecutePipeline(CancellationToken token) =>
            await SendRequestEvent(_currentRequest.Uri, _currentRequest.Content, token);


        public void Dispose()
        {
            _tokenSource.Cancel();

            _queue.NewValuesEvent -= RecieveDataQueue;
            _queue.NewValueEvent -= RecieveDataQueue;
        }
    }
}