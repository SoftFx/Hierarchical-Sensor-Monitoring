﻿using System;
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


        public abstract TimeSpan Delay { get; }


        protected BaseDelayedBackgroundService()
        {
            _logger = LogManager.GetLogger(GetType().Name);

            _logger.Info($"{_logger.Name} is initialized!");
        }


        protected abstract Task ServiceActionAsync();

        protected override Task ExecuteAsync(CancellationToken token)
        {
            return PeriodicTask.Run(ServiceActionAsync, DateTime.UtcNow.Ceil(Delay) - DateTime.UtcNow, Delay, token);
        }

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
