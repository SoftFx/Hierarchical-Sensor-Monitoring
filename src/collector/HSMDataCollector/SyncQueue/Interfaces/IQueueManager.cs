using HSMDataCollector.SyncQueue.BaseQueue;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SyncQueue
{
    internal interface IQueueManager : IDisposable
    {
        ISyncQueue<SensorValueBase> Data { get; }

        ICommandQueue Commands { get; }


        event Action<PackageSendingInfo> PackageRequestInfoEvent;
        event Action<string, PackageInfo> PackageInfoEvent;
        event Action<string, int> OverflowInfoEvent;


        void Init();

        void Stop();
    }
}