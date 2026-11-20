using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using HSMCommon.Extensions;
using HSMServer.Core.Threading;


namespace HSMServer.BackgroundServices
{
    public abstract class BaseDelayedBackgroundService : BackgroundService
    {
        protected readonly Logger _logger;

        public virtual TimeSpan StartDelay { get; } = TimeSpan.Zero;

        public abstract TimeSpan Delay { get; }


        protected BaseDelayedBackgroundService()
        {
            _logger = LogManager.GetLogger(GetType().Name);

            _logger.Info($"{_logger.Name} is initialized!");
        }


        protected abstract Task ServiceActionAsync(CancellationToken token = default);


        protected override Task ExecuteAsync(CancellationToken token)
        {
            var now = DateTime.UtcNow;
            var firstRun = FirstRunUtc(now);

            OnScheduled(firstRun);

            // The logger is load-bearing: PeriodicTask.Run swallows action throws into
            // logger?.Error, so without it a throw out of ServiceActionAsync (e.g. a failed
            // RemoveSensorAsync aborting the self-destroy sweep mid-loop) skips every remaining
            // sensor for that tick with zero output.
            return PeriodicTask.Run(() => ServiceActionAsync(token), firstRun - now, Delay, token, _logger);
        }

        /// <summary>The first run of a service started at <paramref name="utcNow"/>: after the start delay, on the next point of the Delay grid.</summary>
        internal DateTime FirstRunUtc(DateTime utcNow) => (utcNow + StartDelay).Ceil(Delay);

        /// <summary>Called once when the service starts, with the UTC time of its first run.</summary>
        protected virtual void OnScheduled(DateTime firstRunUtc) { }

        protected virtual void RunAction(Action action)
        {
            if (action != null)
            {
                var methodName = action.Method.Name;

                try
                {
                    _logger.Info($"Start {methodName}");

                    action.Invoke();

                    _logger.Info($"Stop {methodName}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"{methodName} failed: {ex}");
                }
            }
        }

        protected virtual async Task RunActionAsync(Func<Task> action, string methodName = null)
        {
            methodName ??= action.Method.Name;

            try
            {
                _logger.Info($"Start method: {methodName}");

                await action();

                _logger.Info($"Stop method: {methodName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"{methodName} failed: {ex}");
            }
        }
    }
}
