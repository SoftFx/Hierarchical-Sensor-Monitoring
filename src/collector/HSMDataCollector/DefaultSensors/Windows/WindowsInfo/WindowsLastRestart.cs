using HSMDataCollector.Options;
using System;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        private const string _categoryName = "System";
        private const string _counterName = "System Up Time";

        private readonly PerformanceCounter _performanceCounter;


        protected override string SensorName => "Windows last restart";


        public WindowsLastRestart(SensorOptions options) : base(options)
        {
            _performanceCounter = new PerformanceCounter(_categoryName, _counterName);
            _performanceCounter.NextValue();
        }


        protected override TimeSpan GetValue() => TimeSpan.FromSeconds(_performanceCounter.NextValue());

        internal override void Stop()
        {
            base.Stop();

            _performanceCounter?.Dispose();
        }
    }
}
