using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class CPUSensor : StandardPerformanceSensorBase
    {
        private readonly BarSensorInt _valuesSensor;
        public CPUSensor(string productKey, string serverAddress, IValuesQueue queue) : base($"{TextConstants.PerformanceNodeName}/CPU usage", "Processor", "% Processor Time", "_Total")
        {
            _valuesSensor = new BarSensorInt($"{TextConstants.PerformanceNodeName}/CPU usage", productKey, serverAddress, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            _valuesSensor.AddValue((int)_internalCounter.NextValue());
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
