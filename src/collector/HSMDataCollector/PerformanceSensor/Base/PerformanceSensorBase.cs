using HSMDataCollector.Base;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;

namespace HSMDataCollector.PerformanceSensor.Base
{
    abstract class PerformanceSensorBase : IPerformanceSensor, ISensor
    {
        private static readonly TimeSpan _monitoringSpan = TimeSpan.FromSeconds(5);

        protected readonly Timer _monitoringTimer;


        public string Path { get; private set; }

        public bool HasLastValue => true;


        internal PerformanceSensorBase(string path)
        {
            Path = path;
            _monitoringTimer = new Timer(OnMonitoringTimerTick, null, _monitoringSpan, _monitoringSpan);
        }


        public abstract void Dispose();

        public abstract SensorValueBase GetLastValue();

        protected abstract void OnMonitoringTimerTick(object state);
    }
}
