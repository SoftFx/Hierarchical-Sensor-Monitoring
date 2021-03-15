using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.Base
{
    abstract class PerformanceSensorBase : IPerformanceSensor, ISensor
    {
        private readonly PerformanceCounter _internalCounter;
        protected readonly Timer _monitoringTimer;
        private static readonly TimeSpan _monitoringSpan = TimeSpan.FromSeconds(5);
        private readonly string _path;
        internal PerformanceSensorBase(string path)
        {
            _path = path;
            _monitoringTimer = new Timer(OnMonitoringTimerTick, null, _monitoringSpan, _monitoringSpan);
        }

        protected abstract void OnMonitoringTimerTick(object state);
        public abstract void Dispose();
        public abstract CommonSensorValue GetLastValue();

        public string Path => _path;
    }
}
