using System.Diagnostics;
using System.Linq;


namespace HSMDataCollector.DefaultSensors.Windows
{
    /// <summary>
    /// Production <see cref="IPerformanceCounterFactory"/> that wraps the real Windows
    /// <see cref="PerformanceCounter"/> / <see cref="PerformanceCounterCategory"/> APIs. All direct use
    /// of those Windows-only types is confined to this class, so the sensor classes stay platform-neutral.
    /// </summary>
    internal sealed class WindowsPerformanceCounterFactory : IPerformanceCounterFactory
    {
        internal static readonly WindowsPerformanceCounterFactory Instance = new WindowsPerformanceCounterFactory();

        public IPerformanceCounter Create(string category, string counter, string instanceFilter = null)
        {
            if (string.IsNullOrEmpty(instanceFilter))
                return new WindowsPerformanceCounter(new PerformanceCounter(category, counter));

            var resolvedInstance = new PerformanceCounterCategory(category)
                .GetInstanceNames()
                .FirstOrDefault(name => name.Contains(instanceFilter));

            if (resolvedInstance == null)
                return null;

            return new WindowsPerformanceCounter(new PerformanceCounter(category, counter, resolvedInstance));
        }


        private sealed class WindowsPerformanceCounter : IPerformanceCounter
        {
            private readonly PerformanceCounter _counter;

            internal WindowsPerformanceCounter(PerformanceCounter counter) => _counter = counter;

            public double NextValue() => _counter.NextValue();

            public void Dispose() => _counter.Dispose();
        }
    }
}
