﻿using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsLastRestart : MonitoringSensorBase<TimeSpan>
    {
        internal const string CounterName = "System Up Time";
        internal const string CategoryName = "System";

        private readonly PerformanceCounter _performanceCounter;

        protected override TimeSpan TimerDueTime => PostTimePeriod.GetTimerDueTime();


        public WindowsLastRestart(WindowsInfoSensorOptions options) : base(options)
        {
            _performanceCounter = new PerformanceCounter(CategoryName, CounterName);
            _performanceCounter.NextValue(); // the first value is always 0
        }


        protected override TimeSpan GetValue() => TimeSpan.FromSeconds(_performanceCounter.NextValue());

        internal override Task Stop()
        {
            _performanceCounter?.Dispose();

            return base.Stop();
        }
    }
}
