using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal sealed class ProcessThreadCountSensor : StandardPerformanceSensorBase<int>
    {
        private const string SensorName = "Process thread count";


        public ProcessThreadCountSensor(string productKey, IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? TextConstants.CurrentProcessNodeName}/{SensorName}", "Process", "Thread Count", processName, GetProcessThreadCountFunc())
        {
            _internalBar = new BarSensor<int>(Path, productKey, queue, SensorType.IntegerBarSensor);
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
                _internalBar.AddValue((int)_internalCounter.NextValue());
            }
            catch { }
        }

        private static Func<double> GetProcessThreadCountFunc()
        {
            double func()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return currentProcess.Threads.Count;
            }

            return func;
        }
    }
}
