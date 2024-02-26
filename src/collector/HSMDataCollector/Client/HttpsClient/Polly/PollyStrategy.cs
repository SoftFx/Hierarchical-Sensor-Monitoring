using HSMDataCollector.Client.HttpsClient.Polly.Strategy;
using HSMDataCollector.Logging;
using Polly;
using Polly.Fallback;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Threading;
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
            .HandleResult(r => r is null || r.StatusCode.IsRetryCode());


        internal ResiliencePipeline<HttpResponseMessage> CommandsPipeline { get; }

        internal ResiliencePipeline<HttpResponseMessage> DataPipeline { get; }


        internal PollyStrategy(ILoggerManager logger)
        {
            _logger = logger;
            
            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>()
            {
                MaxRetryAttempts = MaxRetryAttempts,
                ShouldHandle = arguments => new ValueTask<bool>(!PollyHelper.IsConnected ?? true),
                DelayGenerator = args => new ValueTask<TimeSpan?>(BuildDelay(args.AttemptNumber)),
                OnRetry = args => {
                    PollyHelper.InitRetry(args, out var id);

                    if (args.AttemptNumber != 0)
                        _logger.Error($"Failed to connect to the server. Attempt number {args.AttemptNumber + 1}. Id = {id}");
                    else if (args.Context.Properties.TryGetValue(PollyHelper.DataKey, out var data))
                        _logger.Error($"Failed to send data. Id = {id}. StatusCode={args.Outcome.Result?.StatusCode}. Data={data}.");
                    
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
                    PollyHelper.InitFallBack(arguments.Context, out var id);
                    _logger.Error($"Couldn't establish connection for a long time. Id = {id}");
                     
                    return default;
                }
            };

            var resultStrategy = new ConnectionStrategyOptions<HttpResponseMessage>()
            {
                OnConnectionResult = args => {
                    PollyHelper.IsConnected = args.Outcome.Result != null && !args.Outcome.Result.StatusCode.IsRetryCode();
                    return default;
                }
            };

            DataPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackOptions)
                .AddRetry(retryOptions)
                .AddConnectionCheck(resultStrategy)
                .Build();

            retryOptions.MaxRetryAttempts = int.MaxValue;

            CommandsPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddFallback(fallbackOptions)
                .AddRetry(retryOptions)
                .AddConnectionCheck(resultStrategy)
                .Build();
        }


        private static TimeSpan BuildDelay(int curAttemptNumber) => TimeSpan.FromSeconds(Math.Pow(StartSecondsDelay, Math.Min(curAttemptNumber, MaxDelayPowerOfTwo)));
    }

    internal static class PollyHelper
    {
        internal static readonly ResiliencePropertyKey<int> AttemptKey = new ResiliencePropertyKey<int>("attempt");
        
        internal static readonly ResiliencePropertyKey<string> DataKey = new ResiliencePropertyKey<string>("data");
        
        internal static readonly ResiliencePropertyKey<Guid> RetryId = new ResiliencePropertyKey<Guid>("id");

        internal static ResilienceContext GetContext(CancellationTokenSource token) => ResilienceContextPool.Shared.Get(token.Token);

        internal static bool? IsConnected { get; set; }
        
        
        internal static void InitRetry(OnRetryArguments<HttpResponseMessage> args, out Guid id)
        {
            GetId(args.Context, out id);
            args.Context.Properties.Set(AttemptKey, args.AttemptNumber);
        }
        
        internal static void InitFallBack(ResilienceContext context, out Guid id)
        {
            ReturnContext(context);
            context.Properties.TryGetValue(RetryId, out id);
        }
        
        
        private static void ReturnContext(ResilienceContext context) =>  ResilienceContextPool.Shared.Return(context);

        private static void GetId(ResilienceContext context ,out Guid id)
        {
            if (context.Properties.TryGetValue(RetryId, out id))
                return;
            
            id = Guid.NewGuid();
            context.Properties.Set(RetryId, id);
        }
    }
}