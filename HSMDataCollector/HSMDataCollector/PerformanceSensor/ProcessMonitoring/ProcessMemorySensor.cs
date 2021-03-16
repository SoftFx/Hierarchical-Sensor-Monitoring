using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessMemorySensor : StandardPerformanceSensorBase
    {
        private const int _mbDivisor = 1048576;
        private const string _sensorName = "Process memory MB";
        private readonly BarSensorInt _valuesSensor;
        public ProcessMemorySensor(string productKey, string serverAddress, IValuesQueue queue, string processName)
            : base($"{TextConstants.PerformanceNodeName}/{_sensorName}", "Process", "Working set", processName)
        {
            _valuesSensor = new BarSensorInt($"{TextConstants.PerformanceNodeName}/{_sensorName}", productKey, serverAddress, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                _valuesSensor.AddValue((int)_internalCounter.NextValue() / _mbDivisor);
            }
            catch (Exception e)
            { }
            
        }

        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            _internalCounter?.Dispose();
            _valuesSensor?.Dispose();
        }

        public override CommonSensorValue GetLastValue()
        {
            return _valuesSensor.GetLastValue();
        }
    }
}
