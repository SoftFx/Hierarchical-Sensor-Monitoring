using System;
using System.Collections.Generic;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        //void EnqueueValue(SensorValueBase value);
        [Obsolete("07.07.2021. Use SendValues.")]
        event EventHandler<List<CommonSensorValue>> SendData;

        event EventHandler<List<FullSensorValue>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        [Obsolete("07.07.2021. Use ReturnData.")]
        void ReturnFailedData(List<CommonSensorValue> values);
        [Obsolete("07.07.2021. Use GetCollectedData.")]
        List<CommonSensorValue> GetAllCollectedData();
        void ReturnData(List<FullSensorValue> values);
        List<FullSensorValue> GetCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();

        bool Disposed { get; }
    }
}