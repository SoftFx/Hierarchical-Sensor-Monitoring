using HSMDataCollector.Bar;
using HSMDataCollector.Core;
using HSMDataCollector.PerformanceSensor.StandardSensor;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.ProcessMonitoring
{
    internal sealed class ProcessMemorySensor : StandardPerformanceSensorBase<int>
    {
        private const int MbDivisor = 1048576;
        private const string SensorName = "Process memory MB";


        public ProcessMemorySensor(string productKey, IValuesQueue queue, string processName, string nodeName)
            : base($"{nodeName ?? TextConstants.CurrentProcessNodeName}/{SensorName}", "Process", "Working set", processName, GetProcessMemoryFunc())
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
                _internalBar.AddValue((int)_internalCounter.NextValue() / MbDivisor);
            }
            catch { }
        }

        private static Func<double> GetProcessMemoryFunc()
        {
            double func()
            {
                Process currentProcess = Process.GetCurrentProcess();
                return currentProcess.WorkingSet64;
            }

            return func;
        }
    }
}
