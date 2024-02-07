using HSMDataCollector.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal abstract class SocketsSensor : MonitoringSensorBase<double>
    {
        private const string CategoryTcp4 = "TCPv4";
        private const string CategoryTcp6 = "TCPv6";

        
        private PerformanceCounter _performanceCounterTCPv4;
        private PerformanceCounter _performanceCounterTCPv6;


        internal protected virtual string CounterName { get; }


        internal protected SocketsSensor(SensorOptions options) : base(options) { }
        
        
        internal override Task<bool> Init()
        {
            try
            {
                _performanceCounterTCPv4 = new PerformanceCounter(CategoryTcp4, CounterName);
                _performanceCounterTCPv6 = new PerformanceCounter(CategoryTcp4, CounterName);
            }
            catch (Exception ex)
            {
                ThrowException(new Exception($"Error initializing performance counter: {CategoryTcp4}/{CounterName}, {CategoryTcp6}/{CounterName}: {ex}"));

                return Task.FromResult(false);
            }

            return base.Init();
        }
        
        
        internal override Task Stop()
        {
            _performanceCounterTCPv4?.Dispose();
            _performanceCounterTCPv6?.Dispose();

            return base.Stop();
        }
        
        protected override double GetValue() => _performanceCounterTCPv4.NextValue() + _performanceCounterTCPv6.NextValue();
    }
}