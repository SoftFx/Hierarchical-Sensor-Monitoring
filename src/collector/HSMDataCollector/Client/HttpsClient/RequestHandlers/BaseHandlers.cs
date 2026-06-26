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

                ShouldHandle = ShouldRetry,
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


        // Retry predicate (#1096). The default Polly pipeline only retried on EXCEPTIONS, so a
        // returned non-success HttpResponseMessage (4xx/5xx) was treated as success and the data
        // was dropped (silent loss). We now also retry server-side 5xx — but ONLY on the bounded
        // pipelines (data/priority/file, MaxRetryAttempts = 10). The command pipeline retries
        // unboundedly (for connection failures while the server restarts); applying result-retry
        // there would let a persistent 5xx hang a command send forever, so commands stay
        // exceptions-only and rely on the per-sensor error dictionary in the response. Client 4xx
        // are permanent (bad payload/auth) and are never retried.
        private ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Exception != null)
                return new ValueTask<bool>(true);

            var response = args.Outcome.Result;
            var isServerError = response != null && (int)response.StatusCode >= 500;

            return new ValueTask<bool>(isServerError && MaxRequestAttempts != int.MaxValue);
        }

        private ValueTask LogException(OnRetryArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Result != null)
                _logger.Error($"Failed to send data. Attempt number = {args.AttemptNumber}| Code = {args.Outcome.Result.StatusCode}");

            else if (args.Outcome.Exception != null)
                    _logger.Error($"Failed to send data. Attempt number = {args.AttemptNumber}| Exception = {args.Outcome.Exception.Message} Inner = {args.Outcome.Exception.InnerException?.Message}");

            return default;
        }

        public async ValueTask<PackageSendingInfo> SendAsync(IEnumerable<T> values, CancellationToken token)
        {
            HttpRequest<T> request = new HttpRequest<T>(values, GetUri(values));
            try
            {
                using (var response = await _pipeline.ExecuteAsync(ExecutePipelineAsync, request, token).ConfigureAwait(false))
                {
                    await HandleRequestResultAsync(response, values, token).ConfigureAwait(false);
                    return new PackageSendingInfo(request.Length, response);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Caller-driven cancellation (e.g. queue stop): propagate so the queue processor's
                // ShutdownMode policy can decide whether to re-enqueue or drop the package.
                // HttpClient's own request timeout also surfaces as OperationCanceledException but
                // without the caller's token being cancelled — that path falls through to the
                // generic catch and becomes a normal retryable Error.
                throw;
            }
            catch (Exception ex)
            {
                LogSendFailure(ex, request);
                return new PackageSendingInfo(request.Length, null, exception: ex.Message);
            }
        }

        public async ValueTask<PackageSendingInfo> SendAsync(T value, CancellationToken token)
        {
            HttpRequest<T> request = new HttpRequest<T>(value, GetUri(value));
            try
            {
                using (var response = await _pipeline.ExecuteAsync(ExecutePipelineAsync, request, token).ConfigureAwait(false))
                {
                    await HandleRequestResultAsync(response, value, token).ConfigureAwait(false);
                    return new PackageSendingInfo(request.Length, response);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // See IEnumerable overload above.
                throw;
            }
            catch (Exception ex)
            {
                LogSendFailure(ex, request);
                return new PackageSendingInfo(request.Length, null, exception: ex.Message);
            }
        }


        private void LogSendFailure(Exception ex, HttpRequest<T> request)
        {
            _logger.Error($"Failed to send data. Attempt number = {MaxRequestAttempts}| Exception = {ex.Message} Inner = {ex.InnerException?.Message} | Payload bytes = {request.Length}");
        }


        private async ValueTask<HttpResponseMessage> ExecutePipelineAsync(HttpRequest<T> request, CancellationToken token)
        {
            using (var content = request.GetContent())
            {
                return await _client.SendRequestAsync(request.Uri, content, token).ConfigureAwait(false);
            }
        }


        public void Dispose()
        {
        }
    }
}
