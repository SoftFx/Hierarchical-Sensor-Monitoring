using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessCPUSensor : StandardPerformanceSensorBase
    {
        private readonly BarSensorDouble _valuesSensor;
        public ProcessCPUSensor(string productKey, string serverAddress, IValuesQueue queue, string processName) 
            : base($"{TextConstants.PerformanceNodeName}/Current process CPU", "Process", "% Processor Time", processName)
        {
            _valuesSensor = new BarSensorDouble($"{TextConstants.PerformanceNodeName}/Current process CPU", productKey, serverAddress, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            _valuesSensor.AddValue(Math.Round(_internalCounter.NextValue(), 2, MidpointRounding.AwayFromZero));
        }

        public override void Dispose()
        {
            _monitoringTimer.Dispose();
            _internalCounter.Dispose();
            _valuesSensor.Dispose();
        }

        public override CommonSensorValue GetLastValue()
        {
            return _valuesSensor.GetLastValue();
        }
    }
}
