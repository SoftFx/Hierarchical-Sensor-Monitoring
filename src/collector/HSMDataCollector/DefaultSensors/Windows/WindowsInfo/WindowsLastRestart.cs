﻿using HSMDataCollector.Options;
using System;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        private const string CategoryName = "System";
        private const string CounterName = "System Up Time";

        private readonly PerformanceCounter _performanceCounter;


        protected override string SensorName => "Last restart";


        public WindowsLastRestart(SensorOptions options) : base(options)
        {
            _performanceCounter = new PerformanceCounter(CategoryName, CounterName);
            _performanceCounter.NextValue(); // the first value is always 0
        }


        protected override TimeSpan GetValue() => TimeSpan.FromSeconds(_performanceCounter.NextValue());

        internal override void Stop()
        {
            base.Stop();

            _performanceCounter?.Dispose();
        }
    }
}
