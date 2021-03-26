using System;
using System.Collections.Generic;
using HSMSensorDataObjects;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        //void EnqueueValue(SensorValueBase value);
        event EventHandler<List<CommonSensorValue>> SendData;
        event EventHandler<DateTime> QueueOverflow;
        void ReturnFailedData(List<CommonSensorValue> values);
        List<CommonSensorValue> GetAllCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();

        bool Disposed { get; }
    }
}