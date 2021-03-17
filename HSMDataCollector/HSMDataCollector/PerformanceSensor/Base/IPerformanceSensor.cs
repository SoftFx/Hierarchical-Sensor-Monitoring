using System;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.Base
{
    internal interface IPerformanceSensor : IDisposable
    {
        string Path { get; }
        CommonSensorValue GetLastValue();
    }
}