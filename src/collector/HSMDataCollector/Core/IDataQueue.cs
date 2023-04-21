using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        event Action<List<SensorValueBase>> SendValuesHandler;
        event Action<FileSensorValue> SendValueHandler;


        void Init();

        void Stop();

        void Flush();


        void Push(SensorValueBase value);

        void PushFailValues(List<SensorValueBase> values);

        void PushFailValue(SensorValueBase value);
    }
}