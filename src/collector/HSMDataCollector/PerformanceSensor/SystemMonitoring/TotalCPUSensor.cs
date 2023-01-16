using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    /// <summary>
    /// The sensor monitors the whole CPU usage, currently works for windows only
    /// </summary>
    internal sealed class TotalCPUSensor : StandardPerformanceSensorBase<int>
    {
        private const string SensorName = "Total CPU";


        public TotalCPUSensor(IValuesQueue queue, string nodeName)
            : base($"{nodeName ?? TextConstants.PerformanceNodeName}/{SensorName}", "Processor", "% Processor Time", "_Total", GetTotalCPUFunc())
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

        private static Func<double> GetTotalCPUFunc()
        {
            double func() => 0.0;

            return func;
        }
    }
}
