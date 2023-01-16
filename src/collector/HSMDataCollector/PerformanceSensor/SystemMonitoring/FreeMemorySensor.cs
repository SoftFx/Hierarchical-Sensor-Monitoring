using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    /// <summary>
    /// Sensor that monitors free RAM, currently supported on windows only
    /// </summary>
    internal sealed class FreeMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const string SensorName = "Free memory MB";


        public FreeMemorySensor(IValuesQueue queue, string nodeName) :
            base($"{nodeName ?? TextConstants.PerformanceNodeName}/{SensorName}", "Memory", "Available MBytes", string.Empty, GetFreeMemoryFunc())
        {
            _internalBar = new BarSensor<int>(Path, queue, SensorType.IntegerBarSensor);
        }


        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            _internalCounter?.Dispose();
            _internalBar?.Dispose();
        }

        public override SensorValueBase GetLastValue()
        {
            return _internalBar.GetLastValue();
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                _internalBar.AddValue((int)_internalCounter.NextValue());
            }
            catch { }
        }

        private static Func<double> GetFreeMemoryFunc()
        {
            double func() => Environment.WorkingSet;

            return func;
        }
    }
}
