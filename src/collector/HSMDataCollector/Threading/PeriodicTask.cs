using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Threading
{
    public static class PeriodicTask
    {
        public static async Task Run(Action action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken, Action<Exception> onError = null)
        {
            long interval = (long)period.TotalMilliseconds;

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            long nextActionTime = Environment.TickCount64;

            nextActionTime += interval;

            if (!cancellationToken.IsCancellationRequested)
                InvokeAction(action, onError);

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long wait = nextActionTime - Environment.TickCount64;

                    while (wait <= 0)
                    {
                        nextActionTime += interval;
                        wait = nextActionTime - Environment.TickCount64;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                nextActionTime += interval;

                if (!cancellationToken.IsCancellationRequested)
                    InvokeAction(action, onError);
            }
        }

        public static async Task Run(Func<Task> action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken, Action<Exception> onError = null)
        {
            long interval = (long)period.TotalMilliseconds;

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            long nextActionTime = Environment.TickCount64;

            nextActionTime += interval;

            if (!cancellationToken.IsCancellationRequested)
                await InvokeActionAsync(action, onError).ConfigureAwait(false);

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    long wait = nextActionTime - Environment.TickCount64;

                    while (wait <= 0)
                    {
                        nextActionTime += interval;
                        wait = nextActionTime - Environment.TickCount64;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                nextActionTime += interval;

                if (!cancellationToken.IsCancellationRequested)
                    await InvokeActionAsync(action, onError).ConfigureAwait(false);
            }
        }

        private static void InvokeAction(Action action, Action<Exception> onError)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        private static async Task InvokeActionAsync(Func<Task> action, Action<Exception> onError)
        {
            try
            {
                await action.Invoke().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

    }
}
