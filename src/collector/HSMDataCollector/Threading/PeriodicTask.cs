using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Threading
{
    public static class PeriodicTask
    {
        public static async Task Run(Action action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            int interval = (int)period.TotalMilliseconds;

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            long nextActionTime = Environment.TickCount;

            nextActionTime += interval;

            if (!cancellationToken.IsCancellationRequested)
                action.Invoke();

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long wait = nextActionTime - Environment.TickCount;

                    while (wait <= 0)
                    {
                        nextActionTime += interval;
                        wait = nextActionTime - Environment.TickCount;
                    }

                    await Task.Delay((int)wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                nextActionTime += interval;

                if (!cancellationToken.IsCancellationRequested)
                    action.Invoke();
            }
        }

        public static async Task Run(Func<Task> action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            int interval = (int)period.TotalMilliseconds;

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            long nextActionTime = Environment.TickCount;

            nextActionTime += interval;

            if (!cancellationToken.IsCancellationRequested)
                await action.Invoke();

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long wait = nextActionTime - Environment.TickCount;

                    while (wait <= 0)
                    {
                        nextActionTime += interval;
                        wait = nextActionTime - Environment.TickCount;
                    }

                    await Task.Delay((int)wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                nextActionTime += interval;

                if (!cancellationToken.IsCancellationRequested)
                    await action.Invoke();
            }
        }

    }
}
