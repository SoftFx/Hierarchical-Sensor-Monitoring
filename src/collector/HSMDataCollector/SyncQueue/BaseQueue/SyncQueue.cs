using HSMDataCollector.SyncQueue.BaseQueue;
using System;
using System.Threading;

namespace HSMDataCollector.SyncQueue
{
    internal abstract class SyncQueue : IDisposable
    {
        private readonly TimeSpan _packageCollectPeriod;
        private Timer _sendTimer;


        protected abstract string QueueName { get; }


        public event Action<string, PackageInfo> PackageInfoEvent;

        public event Action<string, int> OverflowCntEvent;


        protected SyncQueue(TimeSpan collectPeriod)
        {
            _packageCollectPeriod = collectPeriod;
        }


        public abstract void Flush();


        public void Init()
        {
            if (_sendTimer == null)
                _sendTimer = new Timer((_) => Flush(), null, _packageCollectPeriod, _packageCollectPeriod);
        }

        public void Stop()
        {
            Flush();

            _sendTimer?.Dispose();
            _sendTimer = null;
        }

        public void Dispose() => Stop();


        protected void ThrowPackageInfo(PackageInfo info)
        {
            if (info.ValuesCount > 0)
                PackageInfoEvent?.Invoke(QueueName, info);
        }

        protected void ThrowQueueOverflowCount(int count)
        {
            if (count > 0)
                OverflowCntEvent?.Invoke(QueueName, count);
        }
    }
}