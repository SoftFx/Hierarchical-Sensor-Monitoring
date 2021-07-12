using HSMDataCollector.Bar;
using HSMDataCollector.PerformanceSensor.Base;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.StandardSensor
{
    abstract class StandardPerformanceSensorBase<T> : PerformanceSensorBase where T: struct
    {
        protected readonly PerformanceCounter InternalCounter;
        protected BarSensor<T> InternalBar;
        protected StandardPerformanceSensorBase(string path, string categoryName, string counterName, string instanceName) : base(path)
        {
            InternalCounter = string.IsNullOrEmpty(instanceName)
                ? new PerformanceCounter(categoryName, counterName)
                : new PerformanceCounter(categoryName, counterName, instanceName);
        }
    }
}
