﻿using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using System;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessThreadCountSensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Process thread count";
        public ProcessThreadCountSensor(string productKey, IValuesQueue queue, string processName)
            : base($"{TextConstants.PerformanceNodeName}/{_sensorName}", "Process", "Thread Count", processName)
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
            throw new NotImplementedException();
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
