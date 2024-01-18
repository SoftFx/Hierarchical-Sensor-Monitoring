using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Fallback;
using Polly.Retry;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public sealed class PollyStrategy
    {
        private const int MaxAttempt = 8;


        private static readonly TimeSpan _startDelay = TimeSpan.FromSeconds(2);

        private readonly PredicateBuilder<HttpResponseMessage> _fallbackHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode.IsRetryCode());

        internal ResiliencePipeline<HttpResponseMessage> Pipeline { get; }


        public PollyStrategy()
        {
            var retryStrategyOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = 10,
                ShouldHandle = arguments => new ValueTask<bool>(arguments.Outcome.Result?.StatusCode.IsRetryCode() ?? true),
                DelayGenerator = args =>
                {
                    if (args.AttemptNumber >= MaxAttempt)
                        return new ValueTask<TimeSpan?>(TimeSpan.FromMinutes(5));

                    var delay = TimeSpan.FromSeconds(Math.Pow(_startDelay.Seconds, args.AttemptNumber));

                    return new ValueTask<TimeSpan?>(delay);
                }
            };

            var fallbackStrategyOptions = new FallbackStrategyOptions<HttpResponseMessage>()
            {
                ShouldHandle = _fallbackHandle,
                FallbackAction = args => args.Outcome.Result != null 
                    ? Outcome.FromResultAsValueTask(args.Outcome.Result) 
                    : Outcome.FromExceptionAsValueTask<HttpResponseMessage>(args.Outcome.Exception),
            };

            Pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackStrategyOptions)
                .AddRetry(retryStrategyOptions)
                .Build();
        }
    }
}