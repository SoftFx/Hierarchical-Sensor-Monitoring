using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMDataCollector.PerformanceSensor.SystemMonitoring
{
    /// <summary>
    /// Sensor that monitors free RAM, currently supported on windows only
    /// </summary>
    internal class FreeMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Free memory MB";
        public FreeMemorySensor(string productKey, IValuesQueue queue,
            string nodeName = TextConstants.PerformanceNodeName) : 
            base($"{nodeName}/{_sensorName}", "Memory", "Available MBytes", string.Empty, GetFreeMemoryFunc())
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

        public override UnitedSensorValue GetLastValue()
        {
            return InternalBar.GetLastValue();
        }

        private static Func<double> GetFreeMemoryFunc()
        {
            Func<double> func = () => Environment.WorkingSet;
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
