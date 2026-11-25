using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions.Files
{
    // The end checks of large files running on this server (#1884). A check runs beside its connection,
    // which may close meanwhile; at shutdown every check is cancelled and awaited before the store and the
    // database close (a cancelled check changes nothing on the disk, and the collector ends the upload
    // again after the restart).
    internal sealed class FileChecks
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentDictionary<Task, byte> _running = new();


        public CancellationToken Stopping => _stopping.Token;

        public int Running => _running.Count;


        // Runs `check` on the thread pool and keeps it until it ends.
        public Task<T> Start<T>(Func<CancellationToken, Task<T>> check)
        {
            var task = Task.Run(() => check(_stopping.Token));

            _running.TryAdd(task, 0);
            task.ContinueWith(done => _running.TryRemove(done, out _), TaskScheduler.Default);

            return task;
        }

        // Cancels every running check and waits for them, at most `bound`. False when some did not end.
        public async Task<bool> StopAsync(TimeSpan bound)
        {
            _stopping.Cancel();

            var running = _running.Keys.ToArray();

            if (running.Length == 0)
                return true;

            try
            {
                await Task.WhenAll(running).WaitAsync(bound);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception)
            {
                // Each check records its own end; a fault here is not the shutdown's.
            }

            return true;
        }
    }
}
