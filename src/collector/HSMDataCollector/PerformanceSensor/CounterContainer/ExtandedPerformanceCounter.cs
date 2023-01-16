using System;
using System.Diagnostics;

namespace HSMDataCollector.PerformanceSensor.CounterContainer
{
    internal sealed class ExtendedPerformanceCounter : IExtendedPerformanceCounter
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


        public void Dispose()
        {
            _internalCounter?.Dispose();
        }

        public double NextValue()
        {
            if (_isUnixSystem)
            {
                return _unixFunc.Invoke();
            }

            return _internalCounter.NextValue();
        }
    }
}
