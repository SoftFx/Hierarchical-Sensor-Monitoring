using System;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public abstract class BaseSocketsSensor : MonitoringSensorBase<int?, NoDisplayUnit>
    {
        private const string CategoryTcp4 = "TCPv4";
        private const string CategoryTcp6 = "TCPv6";

        private IPerformanceCounter _counterTCPv4;
        private IPerformanceCounter _counterTCPv6;


        protected abstract string CounterName { get; }

        // Source of performance counters. Defaults to the real Windows API; overridden in tests with a
        // fake. Internal-virtual so only same-assembly / friend-assembly (test) subclasses can substitute it.
        internal virtual IPerformanceCounterFactory PerformanceCounterFactory => WindowsPerformanceCounterFactory.Instance;


        protected internal BaseSocketsSensor(MonitoringInstantSensorOptions options) : base(options) { }


        public override ValueTask<bool> InitAsync()
        {
            try
            {
                _counterTCPv4 = PerformanceCounterFactory.Create(CategoryTcp4, CounterName);
                _counterTCPv6 = PerformanceCounterFactory.Create(CategoryTcp6, CounterName);
            }
            catch (Exception ex)
            {
                _counterTCPv4?.Dispose();
                _counterTCPv4 = null;
                _counterTCPv6?.Dispose();
                _counterTCPv6 = null;

                HandleException(new Exception($"Error initializing performance counter: {CategoryTcp4}/{CounterName}, {CategoryTcp6}/{CounterName}: {ex}"));

                return new ValueTask<bool>(false);
            }

            return base.InitAsync();
        }

        public override ValueTask StopAsync()
        {
            _counterTCPv4?.Dispose();
            _counterTCPv6?.Dispose();

            return base.StopAsync();
        }

        protected override int? GetValue() => (int?)(_counterTCPv4.NextValue() + _counterTCPv6.NextValue());
    }
}
