﻿using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const int _mbDivisor = 1048576;
        private const string _sensorName = "Process memory MB";
        public ProcessMemorySensor(string productKey, IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? TextConstants.CurrentProcessNodeName}/{_sensorName}", "Process", "Working set", processName, GetProcessMemoryFunc())
        {
            InternalBar = new BarSensor<int>(Path, productKey, queue, SensorType.IntegerBarSensor);
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

        public override SensorValueBase GetLastValue()
        {
            return InternalBar.GetLastValue();
        }

        private static Func<double> GetProcessMemoryFunc()
        {
            Func<double> func = delegate ()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return currentProcess.WorkingSet64;
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
