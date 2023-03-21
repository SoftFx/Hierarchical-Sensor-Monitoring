using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        bool Disposed { get; }


        event Action<List<SensorValueBase>> SendValues;
        event Action<FileSensorValue> FileReceiving;


        void ReturnData(List<SensorValueBase> values);

        void ReturnFile(FileSensorValue file);

        List<SensorValueBase> GetCollectedData();

        void InitializeTimer();

        void Stop();

        void Clear();
    }
}