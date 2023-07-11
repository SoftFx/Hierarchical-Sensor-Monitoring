using HSMDataCollector.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        private const string CategoryName = "System";
        private const string CounterName = "System Up Time";

        private readonly PerformanceCounter _performanceCounter;


        protected override string SensorName => "Last restart";


        public WindowsLastRestart(MonitoringSensorOptions options) : base(options)
        {
            _performanceCounter = new PerformanceCounter(CategoryName, CounterName);
            _performanceCounter.NextValue(); // the first value is always 0
        }


        internal override Task<bool> Start() //send data on start
        {
            OnTimerTick();

            return base.Start();
        }

        protected override TimeSpan GetValue() => TimeSpan.FromSeconds(_performanceCounter.NextValue());

        internal override Task Stop()
        {
            _performanceCounter?.Dispose();
            
            return base.Stop();
        }
    }
}
