using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.BaseQueue;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue
{
    internal sealed class QueueManager : IQueueManager
    {
        private readonly List<SyncQueue> _queueList = new List<SyncQueue>();


        public ISyncQueue<SensorValueBase> Data { get; }

        public ICommandQueue Commands { get; }


        public event Action<PackageSendingInfo> PackageRequestInfoEvent;
        public event Action<string, PackageInfo> PackageInfoEvent;
        public event Action<string, int> OverflowInfoEvent;


        internal QueueManager(CollectorOptions options, ILoggerManager logger) : base()
        {
            Commands = RegisterQueue(new CommandsQueue(options, logger));
            Data = RegisterQueue(new SensorDataQueue(options));
        }


        public void Init() => _queueList.ForEach(q => q.Init());

        public void Stop() => _queueList.ForEach(q => q.Stop());


        public void Dispose()
        {
            foreach (var queue in _queueList)
            {
                queue.PackageRequestInfoEvent -= ThrowPackageRequestInfo;
                queue.OverflowCntEvent -= ThrowOverflowInfo;
                queue.PackageInfoEvent -= ThrowPackageInfo;
                queue.Dispose();
            }
        }


        private T RegisterQueue<T>(T queue) where T : SyncQueue
        {
            _queueList.Add(queue);

            queue.PackageRequestInfoEvent += ThrowPackageRequestInfo;
            queue.OverflowCntEvent += ThrowOverflowInfo;
            queue.PackageInfoEvent += ThrowPackageInfo;

            return queue;
        }


        private void ThrowPackageRequestInfo(PackageSendingInfo info) => PackageRequestInfoEvent?.Invoke(info);

        private void ThrowPackageInfo(string queue, PackageInfo info) => PackageInfoEvent?.Invoke(queue, info);

        private void ThrowOverflowInfo(string queue, int valuesCnt) => OverflowInfoEvent?.Invoke(queue, valuesCnt);
    }
}