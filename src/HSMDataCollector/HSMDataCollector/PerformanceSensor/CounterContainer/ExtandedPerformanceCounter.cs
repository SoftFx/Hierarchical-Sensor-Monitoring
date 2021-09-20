using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.CounterContainer
{
    internal class ExtendedPerformanceCounter : IExtendedPerformanceCounter
    {
        private readonly bool _isUnixSystem;
        private readonly PerformanceCounter _internalCounter;
        private readonly Func<double> _unixFunc;
        internal ExtendedPerformanceCounter(bool isUnix, PerformanceCounter internalCounter, Func<double> unixFunc)
        {
            _internalCounter = internalCounter;
            _isUnixSystem = isUnix;
            _unixFunc = unixFunc;
        }

        public double NextValue()
        {
            if (_isUnixSystem)
            {
                return _unixFunc.Invoke();
            }

            return _internalCounter.NextValue();
        }

        public void Dispose()
        {
            _internalCounter?.Dispose();
        }
    }
}
