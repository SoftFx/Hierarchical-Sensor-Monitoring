using System;
using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const int _mbDivisor = 1048576;
        private const string _sensorName = "Process memory MB";
        public ProcessMemorySensor(string productKey, IValuesQueue queue, string processName,
            string nodeName = TextConstants.CurrentProcessNodeName)
            : base($"{nodeName}/{_sensorName}", "Process", "Working set", processName)
        {
            InternalBar = new BarSensor<int>($"{TextConstants.CurrentProcessNodeName}/{_sensorName}", productKey, queue, SensorType.IntegerBarSensor);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                InternalBar.AddValue((int)InternalCounter.NextValue() / _mbDivisor);
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
