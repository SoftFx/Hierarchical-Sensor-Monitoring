using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.Base
{
    internal interface ISensor : IDisposable
    {
        bool HasLastValue { get; }


        SensorValueBase GetLastValue();
    }
}