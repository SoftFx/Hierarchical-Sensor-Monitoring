using System;
using System.Threading;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// Keeps the deletion of a merged hour's blocks out of a read in progress (#1850). A read decides, hour by hour, whether
    /// it takes an hour from its blocks (not committed when it looked) or from its batches; the blocks of an hour are
    /// deleted only after its commit, so an hour the read takes from blocks keeps them for as long as the read holds the
    /// gate. Reads share it; a deletion takes it alone (with the default delay of a day it never meets a read of that hour,
    /// but a delay of 0 could). Commits do not take it: a commit never removes what a read already decided to use.
    /// </summary>
    public sealed class TableHourGate
    {
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);


        public IDisposable Read()
        {
            _lock.EnterReadLock();
            return new Releaser(_lock.ExitReadLock);
        }

        // Reads waiting for the gate now. For tests.
        internal int WaitingReads => _lock.WaitingReadCount;

        public IDisposable Delete()
        {
            _lock.EnterWriteLock();
            return new Releaser(_lock.ExitWriteLock);
        }


        private sealed class Releaser(Action release) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                    release();
            }
        }
    }
}
