using System.Diagnostics;
using HSMDataCollector.PerformanceSensor.Base;

namespace HSMDataCollector.PerformanceSensor.StandardSensor
{
    abstract class StandardPerformanceSensorBase : PerformanceSensorBase
    {
        protected readonly PerformanceCounter _internalCounter;
        protected StandardPerformanceSensorBase(string path, string categoryName, string counterName, string instanceName) : base(path)
        {
            _internalCounter = string.IsNullOrEmpty(instanceName)
                ? new PerformanceCounter(categoryName, counterName)
                : new PerformanceCounter(categoryName, counterName, instanceName);
        }
    }
}
