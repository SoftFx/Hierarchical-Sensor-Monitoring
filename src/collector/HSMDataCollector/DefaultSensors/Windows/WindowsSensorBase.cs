using HSMDataCollector.Options;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsSensorBase : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        protected const string TotalInstance = "_Total";

        private PerformanceCounter _performanceCounter;


        protected abstract string CategoryName { get; }

        protected abstract string CounterName { get; }

        protected virtual string InstanceName { get; }


        internal WindowsSensorBase(BarSensorOptions options) : base(options) { }


        internal override ValueTask<bool> InitAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(InstanceName))
                    _performanceCounter = new PerformanceCounter(CategoryName, CounterName);
                else
                {
                    var category = new PerformanceCounterCategory(CategoryName);
                    var instantName = category.GetInstanceNames().FirstOrDefault(u => u.Contains(InstanceName));

                    if (instantName == null)
                    {
                        ThrowException(new ArgumentNullException($"Performance counter: {CategoryName}/{CounterName} instance {InstanceName} not found"));

                        return new ValueTask<bool>(false);
                    }

                    _performanceCounter = new PerformanceCounter(CategoryName, CounterName, instantName);
                }
            }
            catch (Exception ex)
            {
                ThrowException(new Exception($"Error initializing performance counter: {CategoryName}/{CounterName} instance {InstanceName}: {ex}"));

                return new ValueTask<bool>(false);
            }

            return base.InitAsync();
        }


        internal override ValueTask StopAsync()
        {
            _performanceCounter?.Dispose();

            return base.StopAsync();
        }

        protected override double GetBarData() => _performanceCounter.NextValue();
    }
}
