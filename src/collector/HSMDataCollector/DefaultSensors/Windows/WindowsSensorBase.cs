using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsSensorBase : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private readonly PerformanceCounter _performanceCounter;


        protected abstract string CategoryName { get; }

        protected abstract string CounterName { get; }

        protected virtual string InstanceName { get; }


        internal WindowsSensorBase(string nodePath) : base(nodePath)
        {
            _performanceCounter = string.IsNullOrEmpty(InstanceName)
                ? new PerformanceCounter(CategoryName, CounterName)
                : new PerformanceCounter(CategoryName, CounterName, InstanceName);
        }


        internal override void Stop()
        {
            base.Stop();

            _performanceCounter?.Dispose();
        }

        protected override double GetBarData() => _performanceCounter.NextValue();
    }
}
