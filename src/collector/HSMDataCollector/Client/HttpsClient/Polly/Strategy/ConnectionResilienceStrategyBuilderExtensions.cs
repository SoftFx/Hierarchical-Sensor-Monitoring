using Polly;

namespace HSMDataCollector.Client.HttpsClient.Polly.Strategy
{
    public static class ConnectionResilienceStrategyBuilderExtensions
    {
        public static ResiliencePipelineBuilder<TResult> AddConnectionCheck<TResult>(this ResiliencePipelineBuilder<TResult> builder, ConnectionStrategyOptions<TResult> options)
        {
            return builder.AddStrategy(
            context =>
            {
                var strategy = new ConnectionResilienceStrategy<TResult>(
                options.ShouldHandle,
                options.OnConnectionResult,
                context.Telemetry);

                return strategy;
            },
            options);
        }
    }
}
