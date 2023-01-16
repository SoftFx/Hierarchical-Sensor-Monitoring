using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal sealed class ProcessCPUSensor : StandardPerformanceSensorBase<double>
    {
        private const string SensorName = "Process CPU";


        public ProcessCPUSensor(IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? DataCollector.CurrentProcessNodeName}/{SensorName}", "Process", "% Processor Time", processName, GetProcessCPUFunc())
        {
            _internalBar = new BarSensor<double>(Path, queue, SensorType.DoubleBarSensor);
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
                _internalBar?.AddValue(Math.Round(_internalCounter?.NextValue() ?? 0.0, 2, MidpointRounding.AwayFromZero));
            }
            catch { }
        }

        private static Func<double> GetProcessCPUFunc()
        {
            double func()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return 100 * currentProcess.PrivilegedProcessorTime.TotalMilliseconds /
                       currentProcess.TotalProcessorTime.TotalMilliseconds;
            }

            return func;
        }
    }
}
