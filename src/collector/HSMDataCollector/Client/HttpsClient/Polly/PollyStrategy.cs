using Polly;
using Polly.Fallback;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public sealed class PollyStrategy
    {
        private const int MaxDelayPowerOfTwo = 8; //max delay is 1024 sec
        private const int StartSecondsDelay = 2;

        private readonly PredicateBuilder<HttpResponseMessage> _fallbackHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r => r.StatusCode.IsRetryCode());

        internal ResiliencePipeline<HttpResponseMessage> CommandsPipeline { get; }

        internal ResiliencePipeline<HttpResponseMessage> DataPipeline { get; }


        public PollyStrategy()
        {
            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = 10,
                ShouldHandle = arguments => {
                    Console.WriteLine(arguments.AttemptNumber);
                    return new ValueTask<bool>(arguments.Outcome.Result?.StatusCode.IsRetryCode() ?? true);
                },
                DelayGenerator = args => new ValueTask<TimeSpan?>(BuildDelay(args.AttemptNumber)),
            };

            var fallbackOptions = new FallbackStrategyOptions<HttpResponseMessage>()
            {
                ShouldHandle = _fallbackHandle,
                FallbackAction = args => args.Outcome.Result != null
                    ? Outcome.FromResultAsValueTask(args.Outcome.Result)
                    : Outcome.FromExceptionAsValueTask<HttpResponseMessage>(args.Outcome.Exception),
            };

            DataPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackOptions)
                .AddRetry(retryOptions)
                .Build();

            retryOptions.MaxRetryAttempts = int.MaxValue;

            CommandsPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackOptions)
                .AddRetry(retryOptions)
                .Build();
        }


        private static TimeSpan BuildDelay(int curAttemptNumber) => TimeSpan.FromSeconds(Math.Pow(StartSecondsDelay, Math.Min(curAttemptNumber, MaxDelayPowerOfTwo)));
    }
}