using HSMServer.Extensions;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public abstract class BaseDelayedBackgroundService : BackgroundService
    {
        protected readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public abstract TimeSpan Delay { get; }


        protected abstract Task ServiceAction();

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            var start = DateTime.UtcNow.Ceil(Delay);

            await Task.Delay(start - DateTime.UtcNow, token);

            while (!token.IsCancellationRequested)
            {
                await ServiceAction();
                await Task.Delay(Delay, token);
            }
        }

        protected virtual void RunAction(Action action)
        {
            var methodName = action.Method.Name;

            try
            {
                _logger.Info($"Start {methodName}");

                action?.Invoke();

                _logger.Info($"Stop {methodName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"{methodName} failed: {ex}");
            }
        }
    }
}
