using System;

namespace HSMDataCollector.PerformanceSensor.Base
{
    internal interface IPerformanceSensor : IDisposable
    {
        string Path { get; }
    }
}