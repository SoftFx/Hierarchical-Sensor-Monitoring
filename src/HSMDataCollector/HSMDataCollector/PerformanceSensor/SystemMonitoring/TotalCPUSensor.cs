using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class TotalCPUSensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Total CPU";
        public TotalCPUSensor(string productKey, IValuesQueue queue)
            : base($"{TextConstants.PerformanceNodeName}/{_sensorName}", "Processor", "% Processor Time", "_Total")
        {
            InternalBar = new BarSensor<int>($"{TextConstants.PerformanceNodeName}/{_sensorName}", productKey, queue, SensorType.IntegerBarSensor);
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

        public override UnitedSensorValue GetLastValueNew()
        {
            return InternalBar.GetLastValueNew();
        }

        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            InternalCounter?.Dispose();
            InternalBar?.Dispose();
        }

        public override CommonSensorValue GetLastValue()
        {
            return InternalBar.GetLastValue();
        }
    }
}
