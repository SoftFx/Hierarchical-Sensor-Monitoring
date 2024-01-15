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


        private readonly PredicateBuilder<HttpResponseMessage> _predicateBuilder = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode.CheckForCodeToRetry());
        
        internal ResiliencePipeline<HttpResponseMessage> Pipeline { get; }


        public event Action<string> Log;


        public PollyStrategy()
        {
            var retryStrategyOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = 10,
                ShouldHandle = arguments =>
                {
                    if (arguments.Outcome.Exception != null)
                        Log?.Invoke(arguments.Outcome.Exception.Message);
                    
                    return new ValueTask<bool>(arguments.Outcome.Result?.StatusCode.CheckForCodeToRetry() ?? true);
                },
                DelayGenerator = args =>
                {
                    var delay = TimeSpan.FromSeconds((long)Math.Pow(_startDelay.Seconds, args.AttemptNumber));

                    return new ValueTask<TimeSpan?>(delay);
                }
            };

            var fallbackStrategyOptions = new FallbackStrategyOptions<HttpResponseMessage>()
            {
                ShouldHandle = _predicateBuilder,
                FallbackAction = args =>
                {
                    if (args.Outcome.Result != null)
                    {
                        Log?.Invoke(args.Outcome.Result.ReasonPhrase);
                        return Outcome.FromResultAsValueTask(args.Outcome.Result);
                    }

                    return Outcome.FromExceptionAsValueTask<HttpResponseMessage>(args.Outcome.Exception);
                },
            };

            Pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackStrategyOptions)
                .AddRetry(retryStrategyOptions)
                .Build();
        }
    }
}