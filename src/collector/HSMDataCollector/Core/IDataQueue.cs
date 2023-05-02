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

        void ReturnSensorValue(SensorValueBase file);

        List<SensorValueBase> DequeueData();

        void InitializeTimer();

        void Stop();
    }
}