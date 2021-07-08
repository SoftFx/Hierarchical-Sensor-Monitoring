using HSMDataCollector.Base;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Diagnostics;
using System.Threading;

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
        public abstract SensorValueBase GetLastValueNew();

        public abstract void Dispose();
        public abstract CommonSensorValue GetLastValue();

        public string Path => _path;
        public bool HasLastValue => true;
    }
}
