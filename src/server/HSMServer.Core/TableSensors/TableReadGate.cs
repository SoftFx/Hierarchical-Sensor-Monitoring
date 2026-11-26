using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// How many reads of table sensors run at once (#1845, #1847): the read route of the Sensor API and the pages of the
    /// web UI (chart, download) share one count, so the UI cannot take more turns than the configured
    /// <c>TableSensors.MaxConcurrentReads</c> beside the route. A read beyond them waits for a turn.
    /// </summary>
    public sealed class TableReadGate
    {
        private readonly SemaphoreSlim _turns;


        public TableReadGate(int maxConcurrentReads)
        {
            if (maxConcurrentReads < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentReads));

            _turns = new SemaphoreSlim(maxConcurrentReads, maxConcurrentReads);
        }


        /// <summary>Waits for a turn; <see cref="Release"/> gives it back. Throws <see cref="OperationCanceledException"/> when cancelled while waiting.</summary>
        public Task WaitAsync(CancellationToken token) => _turns.WaitAsync(token);

        public void Release() => _turns.Release();

        /// <summary>The turns free now. For tests.</summary>
        internal int FreeTurns => _turns.CurrentCount;
    }
}
