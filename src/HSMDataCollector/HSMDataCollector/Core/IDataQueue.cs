using System;
using System.Collections.Generic;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        //void EnqueueValue(CommonSensorValue value);
        [Obsolete("07.07.2021. Use SendValues.")]
        event EventHandler<List<CommonSensorValue>> SendData;

        event EventHandler<List<UnitedSensorValue>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        [Obsolete("07.07.2021. Use ReturnData.")]
        void ReturnFailedData(List<CommonSensorValue> values);
        [Obsolete("07.07.2021. Use GetCollectedData.")]
        List<CommonSensorValue> GetAllCollectedData();
        void ReturnData(List<UnitedSensorValue> values);
        List<UnitedSensorValue> GetCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();

        bool Disposed { get; }
    }
}