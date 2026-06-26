using System;
using System.Threading.Tasks;

namespace HSMDataCollector.Threading
{
    /// <summary>
    /// Runs an OS call that has no timeout of its own with an upper bound (#1102-B2). A corrupted
    /// performance-counter registry (lodctr) or a hung Service Control Manager can block
    /// <c>GetInstanceNames()</c> / <c>GetServices()</c> indefinitely, stalling sensor init/poll
    /// forever. On timeout the caller gets a <see cref="TimeoutException"/> (sensor init fails
    /// gracefully through the regular error path); the abandoned call keeps running on its
    /// ThreadPool thread — a bounded leak, accepted as better than an unbounded hang.
    /// </summary>
    internal static class BoundedBlockingCall
    {
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        internal static T Run<T>(Func<T> call, string description, TimeSpan? timeout = null)
        {
            var effectiveTimeout = timeout ?? DefaultTimeout;
            var task = Task.Run(call);
            var completed = Task.WhenAny(task, Task.Delay(effectiveTimeout)).ConfigureAwait(false).GetAwaiter().GetResult();

            if (!ReferenceEquals(completed, task))
                throw new TimeoutException($"{description} did not complete within {effectiveTimeout}.");

            // Unwraps the original exception instead of AggregateException.
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
