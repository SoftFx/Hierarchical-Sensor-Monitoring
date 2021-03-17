using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class CPUSensor : StandardPerformanceSensorBase
    {
        private readonly BarSensorInt _valuesSensor;
        private const string _sensorName = "CPU usage";
        public CPUSensor(string productKey, string serverAddress, IValuesQueue queue)
            : base($"{TextConstants.PerformanceNodeName}/{_sensorName}", "Processor", "% Processor Time", "_Total")
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
