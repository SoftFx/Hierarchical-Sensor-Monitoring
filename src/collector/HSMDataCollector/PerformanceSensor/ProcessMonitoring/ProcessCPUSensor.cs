﻿using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal class ProcessCPUSensor : StandardPerformanceSensorBase<double>
    {
        private const string _sensorName = "Process CPU";
        public ProcessCPUSensor(string productKey, IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? TextConstants.CurrentProcessNodeName}/{_sensorName}", "Process", "% Processor Time", processName, GetProcessCPUFunc())
        {
            InternalBar = new BarSensor<double>(Path, productKey, queue, SensorType.DoubleBarSensor);
        }

        protected override void OnMonitoringTimerTick(object state)
        {
            try
            {
                InternalBar?.AddValue(Math.Round(InternalCounter?.NextValue() ?? 0.0, 2, MidpointRounding.AwayFromZero));
            }
            catch (Exception e)
            { }
        }

        public override SensorValueBase GetLastValue()
        {
            return InternalBar.GetLastValue();
        }

        private static Func<double> GetProcessCPUFunc()
        {
            Func<double> func = delegate ()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return 100 * currentProcess.PrivilegedProcessorTime.TotalMilliseconds /
                       currentProcess.TotalProcessorTime.TotalMilliseconds;
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
