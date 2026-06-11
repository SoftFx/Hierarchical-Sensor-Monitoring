using System;


namespace HSMDataCollector.DefaultSensors
{
    /// <summary>
    /// Abstraction over a single OS performance metric source. Isolates the Windows-only
    /// <c>System.Diagnostics.PerformanceCounter</c> behind an interface so sensors do not depend on
    /// the platform API directly. This makes the Windows sensors unit-testable on any OS (via a fake)
    /// and leaves room for an alternative provider (native PDH, or a non-Windows source).
    /// </summary>
    internal interface IPerformanceCounter : IDisposable
    {
        /// <summary>Samples the current counter value.</summary>
        double NextValue();
    }


    /// <summary>
    /// Raw access to a performance-counter category: instance enumeration and counter creation for an
    /// already-resolved instance name. This is the thin OS seam under
    /// <see cref="Windows.ProcessAwarePerformanceCounter"/> — the instance-resolution logic on top of it
    /// is platform-neutral and unit-tested with a fake source.
    /// </summary>
    internal interface IPerformanceCounterSource
    {
        string[] GetInstanceNames(string category);

        IPerformanceCounter Create(string category, string counter, string instance);
    }


    /// <summary>
    /// Creates <see cref="IPerformanceCounter"/> instances. The default production implementation wraps
    /// the real Windows performance-counter API; tests substitute a fake. Implementations must be
    /// thread-safe for concurrent <see cref="Create"/> calls.
    /// </summary>
    internal interface IPerformanceCounterFactory
    {
        /// <summary>
        /// Creates a counter for <paramref name="category"/>/<paramref name="counter"/>. When
        /// <paramref name="instanceFilter"/> is non-empty, the factory resolves the first instance whose
        /// name contains it and returns <c>null</c> if no such instance exists (the caller treats this as
        /// "instance not found"). Other failures throw.
        /// </summary>
        IPerformanceCounter Create(string category, string counter, string instanceFilter = null);
    }
}
