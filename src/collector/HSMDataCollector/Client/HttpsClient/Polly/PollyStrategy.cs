using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Fallback;
using Polly.Retry;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public class PollyStrategy
    {
        private static readonly TimeSpan _startDelay = TimeSpan.FromSeconds(2);


        private readonly PredicateBuilder<HttpResponseMessage> _fallbackHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode.CheckForCodeToRetry());

        internal ResiliencePipeline<HttpResponseMessage> Pipeline { get; }


        public PollyStrategy()
        {
            var retryStrategyOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = 10,
                ShouldHandle = arguments => new ValueTask<bool>(arguments.Outcome.Result?.StatusCode.CheckForCodeToRetry() ?? true),
                DelayGenerator = args =>
                {
                    var delay = TimeSpan.FromSeconds((long)Math.Pow(_startDelay.Seconds, args.AttemptNumber));

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