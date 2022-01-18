﻿using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessThreadCountSensor : StandardPerformanceSensorBase<int>
    {
        private const string _sensorName = "Process thread count";
        public ProcessThreadCountSensor(string productKey, IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? TextConstants.CurrentProcessNodeName}/{_sensorName}", "Process", "Thread Count", processName, GetProcessThreadCountFunc())
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

        public override UnitedSensorValue GetLastValue()
        {
            return InternalBar.GetLastValue();
        }

        private static Func<double> GetProcessThreadCountFunc()
        {
            Func<double> func = delegate()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return currentProcess.Threads.Count;
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
