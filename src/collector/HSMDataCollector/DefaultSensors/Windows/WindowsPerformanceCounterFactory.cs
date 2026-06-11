using System.Diagnostics;
using HSMDataCollector.Threading;

namespace HSMDataCollector.DefaultSensors.Windows
{
    /// <summary>
    /// Production <see cref="IPerformanceCounterFactory"/> that wraps the real Windows
    /// <see cref="PerformanceCounter"/> / <see cref="PerformanceCounterCategory"/> APIs. All direct use
    /// of those Windows-only types is confined to this class; the instance-resolution logic lives in
    /// <see cref="PerformanceCounterInstanceResolver"/> / <see cref="ProcessAwarePerformanceCounter"/>
    /// and is unit-tested with a fake <see cref="IPerformanceCounterSource"/>.
    /// </summary>
    internal sealed class WindowsPerformanceCounterFactory : IPerformanceCounterFactory
    {
        internal static readonly WindowsPerformanceCounterFactory Instance = new WindowsPerformanceCounterFactory();

        public IPerformanceCounter Create(string category, string counter, string instanceFilter = null)
        {
            if (string.IsNullOrEmpty(instanceFilter))
                return new WindowsPerformanceCounter(new PerformanceCounter(category, counter));

            var source = WindowsCounterSource.Instance;

            // Current-process counters in per-process categories bind by PID (#1102-E1). Pseudo-
            // instances of the same categories (e.g. "_Global_" for the system-wide time-in-GC
            // sensor) have no PID and resolve by name below.
            if (PerformanceCounterInstanceResolver.ShouldBindByPid(category, instanceFilter, ProcessInfo.CurrentProcessName, out var pidCounterName))
                return ProcessAwarePerformanceCounter.TryCreate(source, category, counter, pidCounterName, instanceFilter, ProcessInfo.CurrentProcessId);

            var resolvedInstance = PerformanceCounterInstanceResolver.ResolveByName(source.GetInstanceNames(category), instanceFilter);

            if (resolvedInstance == null)
                return null;

            return source.Create(category, counter, resolvedInstance);
        }


        private sealed class WindowsCounterSource : IPerformanceCounterSource
        {
            internal static readonly WindowsCounterSource Instance = new WindowsCounterSource();

            public string[] GetInstanceNames(string category) =>
                // A corrupted counter registry can hang this call forever (#1102-B2).
                BoundedBlockingCall.Run(
                    () => new PerformanceCounterCategory(category).GetInstanceNames(),
                    $"PerformanceCounterCategory('{category}').GetInstanceNames()");

            public IPerformanceCounter Create(string category, string counter, string instance) =>
                new WindowsPerformanceCounter(new PerformanceCounter(category, counter, instance));
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
