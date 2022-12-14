using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        event EventHandler<List<UnitedSensorValue>> SendValues;
        event EventHandler<DateTime> QueueOverflow;
        event EventHandler<FileSensorBytesValue> FileReceving;

        void ReturnData(List<UnitedSensorValue> values);
        void ReturnFile(FileSensorBytesValue file);
        List<UnitedSensorValue> GetCollectedData();
        void InitializeTimer();
        void Stop();
        void Clear();
        bool Disposed { get; }
    }
}