using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class FreeMemorySensor : StandardPerformanceSensorBase
    {
        private readonly BarSensorInt _valuesSensor;
        public FreeMemorySensor(string productKey, string serverAddress, IValuesQueue queue) : base($"{TextConstants.PerformanceNodeName}/Free memory MB",
            "Memory", "Available MBytes", string.Empty)
        {
            _valuesSensor = new BarSensorInt($"{TextConstants.PerformanceNodeName}/Free memory MB", productKey, serverAddress, queue);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            _valuesSensor.AddValue((int) _internalCounter.NextValue());
        }
        public override CommonSensorValue GetLastValue()
        {
            return _valuesSensor.GetLastValue();
        }
        public override void Dispose()
        {
            _monitoringTimer.Dispose();
            _internalCounter.Dispose();
            _valuesSensor.Dispose();
        }
    }
}
