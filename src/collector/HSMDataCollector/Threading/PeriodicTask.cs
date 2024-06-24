using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Threading
{
    public static class PeriodicTask
    {

        private static async Task Run(Func<object, CancellationToken, Task> action, object taskState, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
                await action(taskState, cancellationToken).ConfigureAwait(false);

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!cancellationToken.IsCancellationRequested)
                    await action(taskState, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task Run(Action<object> action, object taskState, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
                action(taskState);

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!cancellationToken.IsCancellationRequested)
                    action(taskState);
            }
        }

        public static async Task Run(Action action, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
                action.Invoke();

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!cancellationToken.IsCancellationRequested)
                    action.Invoke();
            }
        }

        public static async Task Run(Func<object, Task> action, object taskState, TimeSpan delay, TimeSpan period, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
                await action(taskState).ConfigureAwait(false);

            if (period == Timeout.InfiniteTimeSpan)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!cancellationToken.IsCancellationRequested)
                    await action(taskState).ConfigureAwait(false);
            }
        }

        public static Task Run(Action<object> action, object taskState, TimeSpan delay, TimeSpan period)
        {
            return Run(action, taskState, delay, period, CancellationToken.None);
        }
    }
}
