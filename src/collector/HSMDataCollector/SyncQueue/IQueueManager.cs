using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.SyncQueue
{
    internal interface IQueueManager : IDisposable
    {
        IDataQueue<BaseRequest> Commands { get; }

        IDataQueue<SensorValueBase> Data { get; }


        void Init();

        void Stop();
    }
}