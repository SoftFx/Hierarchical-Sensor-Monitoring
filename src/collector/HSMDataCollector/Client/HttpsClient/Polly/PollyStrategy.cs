using HSMDataCollector.Logging;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public sealed class PollyStrategy
    {
        private const int MaxDelayPowerOfTwo = 8; //max delay is 1024 sec
        private const int StartSecondsDelay = 2;
        private const int MaxRetryAttempts = 12;

        private readonly ILoggerManager _logger;
        
        private readonly PredicateBuilder<HttpResponseMessage> _fallbackHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r =>r is null || r.StatusCode.IsRetryCode());


        internal static readonly ResiliencePropertyKey<int> AttemptKey = new ResiliencePropertyKey<int>("attempt");
        
        internal static readonly ResiliencePropertyKey<string> DataKey = new ResiliencePropertyKey<string>("data");


        internal ResiliencePipeline<HttpResponseMessage> CommandsPipeline { get; }

        internal ResiliencePipeline<HttpResponseMessage> DataPipeline { get; }


        internal PollyStrategy(ILoggerManager logger)
        {
            _logger = logger;
            
            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = MaxRetryAttempts,
                ShouldHandle = arguments => new ValueTask<bool>(arguments.Outcome.Result?.StatusCode.IsRetryCode() ?? true),
                DelayGenerator = args => new ValueTask<TimeSpan?>(BuildDelay(args.AttemptNumber)),
                OnRetry = args => {
                    args.Context.Properties.Set(AttemptKey, args.AttemptNumber);
                    if (args.AttemptNumber != 0)
                        _logger.Error("Failed to connect to the server");
                    else if (args.Context.Properties.TryGetValue(DataKey, out var data))
                        _logger.Error($"Failed to send data. StatusCode={args.Outcome.Result?.StatusCode}. Data={data}.");
                    
                    return default;
                },
            };

            var fallbackOptions = new FallbackStrategyOptions<HttpResponseMessage>()
            {
                ShouldHandle = _fallbackHandle,
                FallbackAction = args => args.Outcome.Result != null
                    ? Outcome.FromResultAsValueTask(args.Outcome.Result)
                    : Outcome.FromExceptionAsValueTask<HttpResponseMessage>(args.Outcome.Exception),
                OnFallback = arguments => {
                     ResilienceContextPool.Shared.Return(arguments.Context);
                     _logger.Error("Couldn't establish connection for a long time."); // Do smth, mb create new fai; queue?
                     
                     return default;
                }
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