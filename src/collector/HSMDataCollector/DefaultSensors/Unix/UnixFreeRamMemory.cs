using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixFreeRamMemory : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal override string SensorName => "Free memory MB";


        internal UnixFreeRamMemory(string nodePath) : base(nodePath) { }


        protected override double GetBarData() => Environment.WorkingSet;
    }
}
