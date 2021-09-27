using System;

namespace HSMDataCollector.PerformanceSensor.CounterContainer
{
    internal interface IExtendedPerformanceCounter : IDisposable
    {
        double NextValue();
    }
}