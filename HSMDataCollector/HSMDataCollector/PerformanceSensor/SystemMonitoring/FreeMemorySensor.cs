using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class FreeMemorySensor : StandardPerformanceSensorBase
    {
        private readonly BarSensorInt _valuesSensor;
        private const string _sensorName = "Free memory MB";
        public FreeMemorySensor(string productKey, string serverAddress, IValuesQueue queue) : base($"{TextConstants.PerformanceNodeName}/{_sensorName}",
            "Memory", "Available MBytes", string.Empty)
        {
            _valuesSensor = new BarSensorInt($"{TextConstants.PerformanceNodeName}/{_sensorName}", productKey, serverAddress, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                _valuesSensor.AddValue((int)_internalCounter.NextValue());
            }
            catch (Exception e)
            { }
            
        }
        public override CommonSensorValue GetLastValue()
        {
            return _valuesSensor.GetLastValue();
        }
        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            _internalCounter?.Dispose();
            _valuesSensor?.Dispose();
        }
    }
}
