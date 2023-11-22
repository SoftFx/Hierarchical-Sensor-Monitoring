using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SyncQueue
{
    internal interface IQueueManager : IDisposable
    {
        ISyncQueue<SensorValueBase> Data { get; }

        ICommandQueue Commands { get; }


        event Action<string, int> PackageValuesCountInfo;

        event Action<string, int> OverflowInfo;


        void Init();

        void Stop();
    }
}