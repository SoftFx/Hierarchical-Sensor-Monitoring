using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        bool Disposed { get; }


        event EventHandler<List<SensorValueBase>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        event EventHandler<FileSensorValue> FileReceving;


        void ReturnData(List<SensorValueBase> values);

        void ReturnFile(FileSensorValue file);

        List<SensorValueBase> GetCollectedData();

        void InitializeTimer();

        void Stop();

        void Clear();
    }
}