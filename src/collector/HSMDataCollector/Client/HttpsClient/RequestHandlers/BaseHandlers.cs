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

        /// <summary>
        /// How many times one send repeats a failed request before it returns. Zero means a
        /// send is a single attempt and the queue alone decides when to try again.
        /// </summary>
        protected abstract int MaxRetryAttempts { get; }

        /// <summary>The pause between the retries of one send; read only when there are any.</summary>
        protected virtual DelayBackoffType DelayStrategy => DelayBackoffType.Exponential;

        protected int _timeout = 1000;

        protected HsmHttpsClient _client;

        protected BaseHandlers(HsmHttpsClient client, Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;
            _client = client;

            if (MaxRetryAttempts <= 0)
            {
                _pipeline = ResiliencePipeline<HttpResponseMessage>.Empty;
                return;
            }

            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = MaxRetryAttempts,
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


        // Retry predicate of the pipelines that repeat inside a send (data/priority/file,
        // MaxRetryAttempts = 10; #1096). The default Polly pipeline only retried on EXCEPTIONS, so
        // a returned non-success HttpResponseMessage (4xx/5xx) was treated as success and the data
        // was dropped (silent loss). A server-side 5xx is retried too. Client 4xx are permanent
        // (bad payload/auth) and are never retried. The command handler has no in-send retry at
        // all (#1659 A3), so this never runs for it.
        private ValueTask<bool> ShouldRetry(RetryPredicateArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Exception != null)
                return new ValueTask<bool>(true);

            var response = args.Outcome.Result;
            var isServerError = response != null && (int)response.StatusCode >= 500;

            return new ValueTask<bool>(isServerError);
        }

        // A failed attempt that the pipeline is about to repeat. Reported through the client's
        // error sink, which the collector deduplicates (#1659 A4): during an outage every attempt
        // of every send fails the same way, and one entry per window says so. The text therefore
        // names the cause only; the attempt number goes to the debug log.
        private ValueTask LogException(OnRetryArguments<HttpResponseMessage> args)
        {
            if (args.Outcome.Result != null)
                _client.ReportError($"Failed to send data. Code = {(int)args.Outcome.Result.StatusCode} ({args.Outcome.Result.StatusCode})");
            else if (args.Outcome.Exception != null)
                _client.ReportError(DescribeException(args.Outcome.Exception));

            _logger.Debug($"Send attempt {args.AttemptNumber + 1} failed; the send repeats it.");

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
                    return await BuildSendingInfoAsync(request.Length, response).ConfigureAwait(false);
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
                    return await BuildSendingInfoAsync(request.Length, response).ConfigureAwait(false);
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


        // The final failure of a send. The reported text names the cause only, so a cause that
        // repeats is one deduplicated entry; the attempt count and the payload size, which differ
        // from send to send, go to the debug log.
        private void LogSendFailure(Exception ex, HttpRequest<T> request)
        {
            _client.ReportError(DescribeException(ex));
            _logger.Debug($"Failed to send data after {MaxRetryAttempts + 1} attempt(s). Payload bytes = {request.Length}");
        }

        private static string DescribeException(Exception ex) =>
            $"Failed to send data. Exception = {ex.Message} Inner = {ex.InnerException?.Message}";

        // What the server said about a refused request, for the failure text (#1659 A1). Only a
        // plain-text body is a message: the JSON a route answers a 400 with is the request batch
        // itself (/list) or the per-path dictionary (/commands), and sensor values do not belong
        // in a log line. The response is already buffered, so this reads no socket.
        private const int MaxServerMessageLength = 256;

        private static async ValueTask<PackageSendingInfo> BuildSendingInfoAsync(int length, HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return new PackageSendingInfo(length, response);

            var message = await ReadServerMessageAsync(response).ConfigureAwait(false);

            return new PackageSendingInfo(length, (int)response.StatusCode, PackageSendingInfo.DescribeFailure(response, message));
        }

        private static async ValueTask<string> ReadServerMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var content = response.Content;

                if (content == null || !string.Equals(content.Headers.ContentType?.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
                    return null;

                var text = (await content.ReadAsStringAsync().ConfigureAwait(false))?.Trim();

                if (string.IsNullOrEmpty(text))
                    return null;

                text = text.Replace('\r', ' ').Replace('\n', ' ');

                return text.Length <= MaxServerMessageLength ? text : text.Substring(0, MaxServerMessageLength) + "...";
            }
            catch (Exception)
            {
                // The message is a courtesy; the status alone still describes the failure.
                return null;
            }
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
