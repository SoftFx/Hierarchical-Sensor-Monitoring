using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;


namespace HSMServer.Core.Threading
{
    public static class PeriodicTask
    {
        public static async Task Run(Func<Task> action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken, Logger logger = null)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (period == Timeout.InfiniteTimeSpan)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await action().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex);
                    }
                }
                return;
            }

            using var timer = new PeriodicTimer(period);
            try
            {
                do
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            await action().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            logger?.Error(ex);
                        }
                    }
                }
                while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
