using Polly;
using Polly.Telemetry;
using System;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient.Polly.Strategy
{
    internal sealed class ConnectionResilienceStrategy<T> : ResilienceStrategy<T>
    {
        private readonly Func<ConnectionPredicateArguments<T>, ValueTask<bool>> _shouldHandle;
        private readonly Func<OnConnectionResultArguments<T>, ValueTask> _onConnectionResult;
        private readonly ResilienceStrategyTelemetry _telemetry;

        
        public ConnectionResilienceStrategy(Func<ConnectionPredicateArguments<T>, ValueTask<bool>> shouldHandle, Func<OnConnectionResultArguments<T>, ValueTask> onConnectionResult, ResilienceStrategyTelemetry telemetry)
        {
            _shouldHandle = shouldHandle;
            _onConnectionResult = onConnectionResult;
            _telemetry = telemetry;
        }

        
        protected override async ValueTask<Outcome<T>> ExecuteCore<TState>(Func<ResilienceContext, TState, ValueTask<Outcome<T>>> callback, ResilienceContext context, TState state)
        {
            var outcome = await callback(context, state).ConfigureAwait(context.ContinueOnCapturedContext);

            if (await _shouldHandle(new ConnectionPredicateArguments<T>(context, outcome)).ConfigureAwait(context.ContinueOnCapturedContext))
            {
                var args = new OnConnectionResultArguments<T>(context, outcome);

                _telemetry.Report(
                new ResilienceEvent(ResilienceEventSeverity.Information, "Connection"),
                context,
                outcome,
                args);

                await _onConnectionResult(args).ConfigureAwait(context.ContinueOnCapturedContext);
            }

            return outcome;
        }
    }
}
