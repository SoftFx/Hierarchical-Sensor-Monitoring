using System;
using System.Collections.Generic;

namespace HSMDataCollector.DefaultSensors.Windows
{
    /// <summary>
    /// Instance-resolution logic for multi-instance performance-counter categories, extracted from
    /// <see cref="WindowsPerformanceCounterFactory"/> for direct unit testing (#1102-E1). The legacy
    /// behavior — bind the first instance whose name merely contains the filter, once, forever — picks
    /// the WRONG process when instance names collide ("App" also matches "AppService") and silently
    /// switches to ANOTHER process's counter when instance indexes reshuffle after a neighbor exits.
    /// </summary>
    internal static class PerformanceCounterInstanceResolver
    {
        // Per-process categories expose a PID counter that maps an instance name to its process id.
        private static readonly Dictionary<string, string> PidCounterByCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Process"] = "ID Process",
            [".NET CLR Memory"] = "Process ID",
        };

        internal static bool TryGetPidCounterName(string category, out string pidCounterName) =>
            PidCounterByCategory.TryGetValue(category ?? string.Empty, out pidCounterName);

        /// <summary>
        /// PID binding applies only when the filter targets the CURRENT process. Per-process
        /// categories also expose pseudo-instances ("_Global_" in ".NET CLR Memory" — used by the
        /// system-wide time-in-GC sensor) and could in principle be filtered by another process's
        /// name; both must keep resolving by name, not by this process's PID.
        /// </summary>
        internal static bool ShouldBindByPid(string category, string instanceFilter, string currentProcessName, out string pidCounterName)
        {
            pidCounterName = null;

            return string.Equals(instanceFilter, currentProcessName, StringComparison.OrdinalIgnoreCase)
                && TryGetPidCounterName(category, out pidCounterName);
        }

        /// <summary>
        /// Name-based resolution for categories without a PID counter (e.g. "_Total", "C:"). An exact
        /// name match wins over a substring match, so "App" can no longer bind to "AppService".
        /// </summary>
        internal static string ResolveByName(string[] instances, string instanceFilter)
        {
            if (instances == null)
                return null;

            string containsMatch = null;

            foreach (var name in instances)
            {
                if (name == null)
                    continue;

                if (string.Equals(name, instanceFilter, StringComparison.Ordinal))
                    return name;

                if (containsMatch == null && name.Contains(instanceFilter))
                    containsMatch = name;
            }

            return containsMatch;
        }
    }


    /// <summary>
    /// Counter bound to the instance of a specific process in a per-process category. The binding is
    /// established by matching the category's PID counter against the target process id (never by name
    /// alone) and re-validated on every read: when Windows reshuffles instance indexes after a neighbor
    /// process exits ("App#1" becomes "App"), the counter re-resolves instead of silently reporting
    /// another process's data.
    /// </summary>
    internal sealed class ProcessAwarePerformanceCounter : IPerformanceCounter
    {
        private readonly IPerformanceCounterSource _source;
        private readonly string _category;
        private readonly string _counter;
        private readonly string _pidCounterName;
        private readonly string _instanceFilter;
        private readonly int _processId;

        private IPerformanceCounter _valueCounter;
        private IPerformanceCounter _pidCounter;

        private ProcessAwarePerformanceCounter(IPerformanceCounterSource source, string category, string counter, string pidCounterName, string instanceFilter, int processId)
        {
            _source = source;
            _category = category;
            _counter = counter;
            _pidCounterName = pidCounterName;
            _instanceFilter = instanceFilter;
            _processId = processId;
        }

        internal static ProcessAwarePerformanceCounter TryCreate(IPerformanceCounterSource source, string category, string counter, string pidCounterName, string instanceFilter, int processId)
        {
            var counterInstance = new ProcessAwarePerformanceCounter(source, category, counter, pidCounterName, instanceFilter, processId);

            return counterInstance.TryBind() ? counterInstance : null;
        }

        public double NextValue()
        {
            if (!IsBindingStillOurs())
            {
                DisposeCounters();

                if (!TryBind())
                    throw new InvalidOperationException(
                        $"Performance counter instance for '{_instanceFilter}' (pid {_processId}) disappeared from category '{_category}'.");
            }

            return _valueCounter.NextValue();
        }

        public void Dispose() => DisposeCounters();

        private bool IsBindingStillOurs()
        {
            try
            {
                return (int)_pidCounter.NextValue() == _processId;
            }
            catch
            {
                // The bound instance no longer exists (the real PerformanceCounter throws) — rebind.
                return false;
            }
        }

        private bool TryBind()
        {
            foreach (var name in _source.GetInstanceNames(_category) ?? new string[0])
            {
                if (name == null || !name.Contains(_instanceFilter))
                    continue;

                IPerformanceCounter pidCounter = null;
                try
                {
                    pidCounter = _source.Create(_category, _pidCounterName, name);

                    if ((int)pidCounter.NextValue() != _processId)
                    {
                        pidCounter.Dispose();
                        continue;
                    }

                    _pidCounter = pidCounter;
                    _valueCounter = _source.Create(_category, _counter, name);
                    return true;
                }
                catch
                {
                    // The candidate disappeared between enumeration and the PID read — try the next one.
                    pidCounter?.Dispose();
                }
            }

            return false;
        }

        private void DisposeCounters()
        {
            _valueCounter?.Dispose();
            _valueCounter = null;

            _pidCounter?.Dispose();
            _pidCounter = null;
        }
    }
}
