using System;
using System.Threading;

namespace HSMCommon.Threading
{
    public static class ReaderWriterLockSlimExtensions
    {
        public readonly struct ReadLock : IDisposable
        {
            private readonly ReaderWriterLockSlim _locker;

            internal ReadLock(ReaderWriterLockSlim locker)
            {
                locker.EnterReadLock();
                _locker = locker;
            }

            public void Dispose()
            {
                _locker.ExitReadLock();
            }
        }

        public readonly struct WriteLock : IDisposable
        {
            private readonly ReaderWriterLockSlim _locker;

            internal WriteLock(ReaderWriterLockSlim locker)
            {
                locker.EnterWriteLock();
                _locker = locker;
            }

            public void Dispose()
            {
                _locker.ExitWriteLock();
            }
        }

        public readonly struct UpgradeableReadLock : IDisposable
        {
            private readonly ReaderWriterLockSlim _locker;

            internal UpgradeableReadLock(ReaderWriterLockSlim locker)
            {
                locker.EnterUpgradeableReadLock();
                _locker = locker;
            }

            public void Dispose()
            {
                _locker.ExitUpgradeableReadLock();
            }
        }

        public static ReadLock GetReadLock(this ReaderWriterLockSlim locker)
        {
            return new ReadLock(locker);
        }

        public static WriteLock GetWriteLock(this ReaderWriterLockSlim locker)
        {
            return new WriteLock(locker);
        }

        public static UpgradeableReadLock GetUpgradeableReadLock(this ReaderWriterLockSlim locker)
        {
            return new UpgradeableReadLock(locker);
        }

    }
}