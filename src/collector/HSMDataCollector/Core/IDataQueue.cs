using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public interface IDataQueue
    {
        event Action<List<SensorValueBase>> NewValuesEvent;
        event Action<FileSensorValue> NewValueEvent;


        void Init();

        void Stop();

        void Flush();


        void Push(SensorValueBase value);

        void PushFailValue(SensorValueBase value);
    }
}