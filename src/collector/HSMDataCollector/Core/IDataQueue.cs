using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        event EventHandler<List<SensorValueBase>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        void ReturnData(List<SensorValueBase> values);
        List<SensorValueBase> GetCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();
        bool Disposed { get; }
    }
}