using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HSMDataCollector.Options;


namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal abstract class BaseSocketsSensor : MonitoringSensorBase<int>
    {
        private const string CategoryTcp4 = "TCPv4";
        private const string CategoryTcp6 = "TCPv6";

        private PerformanceCounter _counterTCPv4;
        private PerformanceCounter _counterTCPv6;


        protected abstract string CounterName { get; }


        protected internal BaseSocketsSensor(SensorOptions options) : base(options) { }
        
        
        internal override ValueTask<bool> InitAsync()
        {
            try
            {
                _counterTCPv4 = new PerformanceCounter(CategoryTcp4, CounterName);
                _counterTCPv6 = new PerformanceCounter(CategoryTcp6, CounterName);
            }
            catch (Exception ex)
            {
                ThrowException(new Exception($"Error initializing performance counter: {CategoryTcp4}/{CounterName}, {CategoryTcp6}/{CounterName}: {ex}"));

                return new ValueTask<bool>(false);
            }

            return base.InitAsync();
        }
        
        
        internal override ValueTask StopAsync()
        {
            _counterTCPv4?.Dispose();
            _counterTCPv6?.Dispose();

            return base.StopAsync();
        }
        
        protected override int GetValue() => (int)(_counterTCPv4.NextValue() + _counterTCPv6.NextValue());
    }
}