using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        event EventHandler<List<UnitedSensorValue>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        void ReturnData(List<UnitedSensorValue> values);
        List<UnitedSensorValue> GetCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();
        bool Disposed { get; }
    }
}