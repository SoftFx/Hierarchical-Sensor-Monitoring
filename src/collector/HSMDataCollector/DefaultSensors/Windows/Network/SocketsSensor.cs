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

        
        private PerformanceCounter _counterTCPv4;
        private PerformanceCounter _counterTCPv6;


        protected abstract string CounterName { get; }


        protected internal SocketsSensor(SensorOptions options) : base(options) { }
        
        
        internal override Task<bool> Init()
        {
            try
            {
                _counterTCPv4 = new PerformanceCounter(CategoryTcp4, CounterName);
                _counterTCPv6 = new PerformanceCounter(CategoryTcp4, CounterName);
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
            _counterTCPv4?.Dispose();
            _counterTCPv6?.Dispose();

            return base.Stop();
        }
        
        protected override double GetValue() => _counterTCPv4.NextValue() + _counterTCPv6.NextValue();
    }
}