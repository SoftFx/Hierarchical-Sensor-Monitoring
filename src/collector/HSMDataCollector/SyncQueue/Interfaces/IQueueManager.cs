using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SyncQueue
{
    internal interface IQueueManager : IDisposable
    {
        ISyncQueue<SensorValueBase> Data { get; }
        
        ICommandQueue Commands { get; }


        void Init();

        void Stop();
    }
}