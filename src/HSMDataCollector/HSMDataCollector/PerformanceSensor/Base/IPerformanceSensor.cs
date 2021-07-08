using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.PerformanceSensor.Base
{
    internal interface IPerformanceSensor : IDisposable
    {
        string Path { get; }
        CommonSensorValue GetLastValue();
    }
}