using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsSensorBase : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        protected const string TotalInstance = "_Total";

        private IPerformanceCounter _performanceCounter;


        protected abstract string CategoryName { get; }

        protected abstract string CounterName { get; }

        protected virtual string InstanceName { get; }

        // Source of performance counters. Defaults to the real Windows API; overridden in tests with a
        // fake so these sensors can be exercised on any OS. Internal-virtual so only same-assembly /
        // friend-assembly (test) subclasses can substitute it.
        internal virtual IPerformanceCounterFactory PerformanceCounterFactory => WindowsPerformanceCounterFactory.Instance;


        internal WindowsSensorBase(BarSensorOptions options) : base(options) { }


        public override ValueTask<bool> InitAsync()
        {
            try
            {
                _performanceCounter = PerformanceCounterFactory.Create(CategoryName, CounterName, InstanceName);

                if (_performanceCounter == null)
                {
                    HandleException(new ArgumentNullException($"Performance counter: {CategoryName}/{CounterName} instance {InstanceName} not found"));

                    return new ValueTask<bool>(false);
                }
            }
            catch (Exception ex)
            {
                HandleException(new Exception($"Error initializing performance counter: {CategoryName}/{CounterName} instance {InstanceName}: {ex}"));

                return new ValueTask<bool>(false);
            }

            return base.InitAsync();
        }


        public override ValueTask StopAsync()
        {
            _performanceCounter?.Dispose();

            return base.StopAsync();
        }

        protected override double? GetBarData() => _performanceCounter.NextValue();
    }
}
