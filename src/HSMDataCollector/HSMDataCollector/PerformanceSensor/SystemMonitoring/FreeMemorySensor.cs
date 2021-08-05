using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    internal class FreeMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Free memory MB";
        public FreeMemorySensor(string productKey, IValuesQueue queue,
            string nodeName = TextConstants.PerformanceNodeName) : 
            base($"{nodeName}/{_sensorName}", "Memory", "Available MBytes", string.Empty)
        {
            InternalBar = new BarSensor<int>($"{nodeName}/{_sensorName}", productKey, queue, SensorType.IntegerBarSensor);
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

        public override CommonSensorValue GetLastValue()
        {
            return InternalBar.GetLastValue();
        }
        public override void Dispose()
        {
            _monitoringTimer?.Dispose();
            InternalCounter?.Dispose();
            InternalBar?.Dispose();
        }
    }
}
