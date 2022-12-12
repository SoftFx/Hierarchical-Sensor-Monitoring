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
    internal class TotalCPUSensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Total CPU";
        public TotalCPUSensor(string productKey, IValuesQueue queue, string nodeName)
            : base($"{nodeName ?? TextConstants.PerformanceNodeName}/{_sensorName}", "Processor", "% Processor Time", "_Total", GetTotalCPUFunc())
        {
            InternalBar = new BarSensor<int>(Path, productKey, queue, SensorType.IntegerBarSensor);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                InternalBar.AddValue((int)InternalCounter.NextValue());
            }
            catch (Exception e)
            { }
        }

        public override SensorValueBase GetLastValue()
        {
            return InternalBar.GetLastValue();
        }

        private static Func<double> GetTotalCPUFunc()
        {
            Func<double> func = delegate ()
            {
                return 0.0;
            };
            return func;
        }

        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            InternalCounter?.Dispose();
            InternalBar?.Dispose();
        }
    }
}
