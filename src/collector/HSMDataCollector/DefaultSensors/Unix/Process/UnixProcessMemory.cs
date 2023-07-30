﻿using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessMemory : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        protected override string SensorName => "Process memory MB";


        internal UnixProcessMemory(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => ProcessInfo.CurrentProcess.WorkingSet64.BytesToMegabytes();
    }
}
