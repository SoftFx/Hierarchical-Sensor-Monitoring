using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessCPUSensor : StandardPerformanceSensorBase
    {
        private const string _sensorName = "Process CPU";
        private readonly BarSensorDouble _valuesSensor;
        public ProcessCPUSensor(string productKey, IValuesQueue queue, string processName) 
            : base($"{TextConstants.PerformanceNodeName}/{_sensorName}", "Process", "% Processor Time", processName)
        {
            _valuesSensor = new BarSensorDouble($"{TextConstants.PerformanceNodeName}/{_sensorName}", productKey, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                _valuesSensor.AddValue(Math.Round(_internalCounter.NextValue() / Environment.ProcessorCount, 2, MidpointRounding.AwayFromZero));
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
