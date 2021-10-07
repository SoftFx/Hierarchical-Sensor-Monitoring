using HSMDataCollector.Bar;
using HSMDataCollector.PerformanceSensor.Base;
using HSMDataCollector.PerformanceSensor.CounterContainer;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HSMDataCollector.PerformanceSensor.StandardSensor
{
    abstract class StandardPerformanceSensorBase<T> : PerformanceSensorBase where T: struct
    {
        protected readonly IExtendedPerformanceCounter InternalCounter;
        protected BarSensor<T> InternalBar;
        protected StandardPerformanceSensorBase(string path, string categoryName, string counterName, string instanceName, Func<double> unixFunc) : base(path)
        {
            bool isUnix = IsUnixOS();
            if (isUnix)
            {
                InternalCounter = new ExtendedPerformanceCounter(true, null, unixFunc);
            }
            else
            {
                PerformanceCounter windowsCounter = string.IsNullOrEmpty(instanceName)
                    ? new PerformanceCounter(categoryName, counterName)
                    : new PerformanceCounter(categoryName, counterName, instanceName);
                InternalCounter = new ExtendedPerformanceCounter(false, windowsCounter, unixFunc);
            }
        }
        
        private bool IsUnixOS()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                   RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }
    }
}
